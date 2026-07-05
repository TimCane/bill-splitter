using BillSplitter.Api.Configuration;
using BillSplitter.Api.Health;
using BillSplitter.Api.Hubs;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Wiring order is a contract - see docs/07-backend-design.md#programcs-wiring.

// 1. Options binding + validation (fail fast on missing required config).
builder.Services.AddAppOptions(builder.Configuration);

// 2. Framework services: SignalR, controllers, ProblemDetails, rate limiter.
builder.Services.AddSignalR();
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddRateLimiter(options =>
{
    // Per-IP policies land in M7 (docs/10-security-privacy.md).
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// 3. Singletons. Session store, receipt storage, OCR queue, id generator and
//    the scoped SnapshotMapper land with their implementations in M2+; the
//    Redis multiplexer and health probe are needed for /healthz now.
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var redis = builder.Configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions>()!;
    var config = ConfigurationOptions.Parse(redis.ConnectionString);
    // Keep startup alive when Redis is down - /healthz reports it, not crashes.
    config.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(config);
});
builder.Services.AddHttpClient(HealthProbe.HttpClientName);
builder.Services.AddSingleton<HealthProbe>();

// 4. Hosted services: OcrWorker lands in M3.

// 5. Authentication (participant token handler, M2) + HostOnly policy.
builder.Services.AddAuthentication();
builder.Services.AddAuthorization(options =>
    options.AddPolicy("HostOnly", policy => policy.RequireClaim("isHost", "true")));

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

// 6. Pipeline: exception handling -> rate limiter -> auth -> endpoints.
app.UseExceptionHandler();

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
