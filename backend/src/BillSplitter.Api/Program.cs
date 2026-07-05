using BillSplitter.Api.Configuration;
using BillSplitter.Api.Hubs;
using Microsoft.Extensions.Options;

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

// 3. Singletons (Redis multiplexer, session store, receipt storage, OCR queue,
//    id generator), the typed OCR HttpClient and the scoped SnapshotMapper land
//    with their implementations in M2+.

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

// Anonymous liveness/readiness probe. Real Redis/MinIO/OCR checks land in #7;
// email is a static capability flag driven by SMTP configuration.
app.MapGet("/healthz", (IOptions<SmtpOptions> smtp) => Results.Ok(new
{
    redis = true,
    minio = true,
    ocr = true,
    email = smtp.Value.IsEnabled,
}));

app.Run();
