using BillSplitter.Api.Auth;
using BillSplitter.Api.Configuration;
using BillSplitter.Api.Dtos;
using BillSplitter.Api.Health;
using BillSplitter.Api.Hubs;
using BillSplitter.Api.Middleware;
using BillSplitter.Api.Ocr;
using BillSplitter.Domain;
using BillSplitter.Infrastructure.Email;
using BillSplitter.Infrastructure.Identity;
using BillSplitter.Infrastructure.Ocr;
using BillSplitter.Infrastructure.Redis;
using BillSplitter.Infrastructure.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Minio;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Wiring order is a contract - see docs/07-backend-design.md#programcs-wiring.

// 1. Options binding + validation (fail fast on missing required config).
builder.Services.AddAppOptions(builder.Configuration);

// 2. Framework services: SignalR, controllers, ProblemDetails, rate limiter.
builder.Services.AddSignalR();
builder.Services.AddControllers();
builder.Services.AddProblemDetails();

// Per-client-IP rate limits (docs/10-security-privacy.md#rate-limits). Bound
// eagerly - the limiter is built here, not resolved from DI per request.
var rateLimits = new RateLimitOptions();
builder.Configuration.GetSection(RateLimitOptions.SectionName).Bind(rateLimits);
builder.Services.AddRateLimiter(options => RateLimiting.Configure(options, rateLimits));

// Trust the reverse proxy's X-Forwarded-For so the limiter keys on the real
// client, not the proxy (off unless configured - docs/13-deployment.md).
var trustProxy = builder.Services.AddProxyForwardedHeaders(builder.Configuration);

// 3. Singletons. Session store, receipt storage, OCR queue, id generator and
//    the scoped SnapshotMapper land with their implementations in M2+; the
//    Redis multiplexer and health probe are needed for /healthz now.
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var redis = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
    var config = ConfigurationOptions.Parse(redis.ConnectionString);
    // Keep startup alive when Redis is down - /healthz reports it, not crashes.
    config.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(config);
});
builder.Services.AddHttpClient(HealthProbe.HttpClientName);
builder.Services.AddSingleton<HealthProbe>();

builder.Services.AddSingleton<IIdGenerator, IdGenerator>();

builder.Services.AddSingleton<ISessionStore>(sp =>
{
    var mux = sp.GetRequiredService<IConnectionMultiplexer>();
    var ids = sp.GetRequiredService<IIdGenerator>();
    var session = sp.GetRequiredService<IOptions<BillSplitter.Api.Configuration.SessionOptions>>().Value;
    return new RedisSessionStore(
        mux,
        ids,
        TimeSpan.FromHours(session.TtlHours),
        TimeSpan.FromMinutes(session.FinalizedTtlMinutes),
        sp.GetRequiredService<ILogger<RedisSessionStore>>());
});

builder.Services.AddSingleton<IMinioClient>(sp =>
{
    var minio = sp.GetRequiredService<IOptions<MinioOptions>>().Value;
    var endpoint = new Uri(minio.Endpoint);
    return new MinioClient()
        .WithEndpoint(endpoint.Host, endpoint.Port)
        .WithCredentials(minio.AccessKey, minio.SecretKey)
        .WithSSL(endpoint.Scheme == Uri.UriSchemeHttps)
        .Build();
});
builder.Services.AddSingleton<IReceiptStorage>(sp => new MinioReceiptStorage(
    sp.GetRequiredService<IMinioClient>(),
    sp.GetRequiredService<IOptions<MinioOptions>>().Value.Bucket));

// Bounded OCR work queue (producer: create endpoint; consumer: OcrWorker) and
// the typed sidecar client with its own base URL and timeout.
builder.Services.AddSingleton(sp =>
    new OcrQueue(sp.GetRequiredService<IOptions<OcrOptions>>().Value.QueueCapacity));
builder.Services.AddHttpClient<IOcrClient, HttpOcrClient>((sp, client) =>
{
    var ocr = sp.GetRequiredService<IOptions<OcrOptions>>().Value;
    client.BaseAddress = new Uri(ocr.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(ocr.TimeoutSeconds);
});

// Email is optional: a configured relay gets the MailKit sender, otherwise the
// null sender backstops the (hidden) address field (docs/04-api-contract.md#get-healthz).
builder.Services.AddSingleton<IEmailSender>(sp =>
{
    var smtp = sp.GetRequiredService<IOptions<SmtpOptions>>().Value;
    if (!smtp.IsEnabled)
    {
        return new NullEmailSender();
    }

    return new MailKitEmailSender(
        smtp.Host!,
        smtp.Port,
        smtp.Username,
        smtp.Password,
        // A bare Host enables the capability (docs/04-api-contract.md#get-healthz),
        // so the sender must resolve to a parseable address even when From and
        // Username are unset - Host alone is not one and would fault every send.
        smtp.From ?? smtp.Username ?? $"no-reply@{smtp.Host}",
        sp.GetRequiredService<ILogger<MailKitEmailSender>>());
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SnapshotBroadcastCoalescer>();
builder.Services.AddScoped<SnapshotMapper>();
builder.Services.AddScoped<ISessionNotifier, SignalRSessionNotifier>();
builder.Services.AddScoped<StaleOcrRecovery>();

// 4. Hosted services: the OCR worker consuming the queue (max 2 concurrent).
builder.Services.AddHostedService(sp => new OcrWorker(
    sp.GetRequiredService<OcrQueue>(),
    sp.GetRequiredService<IServiceScopeFactory>(),
    sp.GetRequiredService<IOptions<OcrOptions>>().Value.MaxConcurrency,
    sp.GetRequiredService<ILogger<OcrWorker>>()));

// 5. Authentication (participant token handler) + Participant / HostOnly policies.
builder.Services.AddAuthentication(ParticipantAuth.Scheme)
    .AddScheme<AuthenticationSchemeOptions, ParticipantTokenHandler>(ParticipantAuth.Scheme, null);
builder.Services.AddAuthorization(options =>
{
    var participant = new AuthorizationPolicyBuilder(ParticipantAuth.Scheme)
        .RequireAuthenticatedUser()
        .RequireClaim(ParticipantAuth.ParticipantIdClaim)
        .Build();
    options.DefaultPolicy = participant;
    options.AddPolicy(ParticipantAuth.ParticipantPolicy, participant);
    options.AddPolicy(ParticipantAuth.HostPolicy, policy => policy
        .AddAuthenticationSchemes(ParticipantAuth.Scheme)
        .RequireClaim(ParticipantAuth.IsHostClaim, "true"));
});

// SPA is same-origin in production; dev serves it from the Vite origin.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
        .WithOrigins("http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));
}

var app = builder.Build();

// Connect the multiplexer at startup (AbortOnConnectFail=false keeps a down
// Redis from crashing boot) so the first /healthz probe never pays the connect
// cost on the request thread.
_ = app.Services.GetRequiredService<IConnectionMultiplexer>();

// 6. Pipeline: forwarded headers -> exception handling -> rate limiter -> auth.
// Forwarded headers run first so every downstream stage (rate limiter keying,
// HTTPS-aware redirects) sees the real client address and scheme.
if (trustProxy)
{
    app.UseForwardedHeaders();
}

app.UseExceptionHandler();
app.UseMiddleware<DomainExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseCors();
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<SessionHub>("/hubs/session");

// Anonymous readiness probe: 200 only when Redis, MinIO and OCR all answer,
// 503 otherwise. email is a capability flag and never changes the status code.
app.MapGet("/healthz", async (HealthProbe probe, CancellationToken ct) =>
{
    var report = await probe.CheckAsync(ct);
    return Results.Json(
        new { redis = report.Redis, minio = report.Minio, ocr = report.Ocr, email = report.Email },
        statusCode: report.IsHealthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
});

app.Run();

// Exposed so WebApplicationFactory<Program> can host the API in integration tests.
public partial class Program;
