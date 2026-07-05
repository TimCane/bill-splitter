# Backend design

ASP.NET Core on .NET 10. Clean layering per house style, minus EF/Postgres
(this app has no database - Redis is the store).

## Solution layout

```
backend/
  BillSplitter.sln
  Directory.Build.props        # Nullable, ImplicitUsings, TreatWarningsAsErrors, analyzers
  Directory.Packages.props     # central package management
  .editorconfig
  src/
    BillSplitter.Domain/       # aggregate, value types, pure services, interfaces. No outward deps.
    BillSplitter.Infrastructure/ # Redis store, MinIO storage, OCR client, MailKit sender, OCR worker
    BillSplitter.Api/          # controllers, SessionHub, DTOs + mappers, middleware, Program.cs
  tests/
    BillSplitter.Tests/            # xUnit + Moq + FluentAssertions
    BillSplitter.IntegrationTests/ # Testcontainers (redis, minio) + WebApplicationFactory
```

Dependencies flow inward only: `Api -> Infrastructure -> Domain`,
`Api -> Domain`. Domain references nothing.

## Domain project

| Type | Kind | Responsibility |
| --- | --- | --- |
| `Session`, `Participant`, `LineItem`, `Claim`, `Bill`, `OcrInfo` | aggregate | state + invariants ([02-domain-model.md](02-domain-model.md)); all mutations are methods on `Session` that throw `DomainException(code)` on rule violations |
| `SessionState`, `OcrStatus` | enums | |
| `SplitCalculator` | pure static service | largest-remainder distribution + per-participant totals |
| `ReceiptParser` | pure service | OCR lines -> `ParsedReceipt` ([06-ocr-service.md](06-ocr-service.md#parsing)) |
| `OcrResult`, `OcrLine`, `ParsedReceipt` | value types | sidecar output / parser output |
| `ISessionStore` | interface | `GetAsync`, `CreateAsync`, `MutateAsync` (below), `OpenAsync`, `FinalizeAsync` (the CAS write with the TTL shrink in one script), `ResolveCodeAsync` |
| `IReceiptStorage` | interface | `PutAsync`, `GetAsync`, `DeleteAsync` |
| `IOcrClient` | interface | `RecognizeAsync(Stream) -> OcrResult` |
| `IEmailSender` | interface | `SendSummaryAsync(email, snapshot)` |
| `ISessionNotifier` | interface | `SnapshotUpdatedAsync`, `OcrStatusChangedAsync`, `SessionFinalizedAsync` - implemented in Api over `IHubContext` so Domain/Infrastructure never reference SignalR |
| `IIdGenerator` | interface | session/participant/item ids, tokens, short codes (crypto RNG) |
| `DomainException` | exception | carries the stable error `type` code ([04-api-contract.md](04-api-contract.md#errors)) |

### The mutate pattern

All writes go through one funnel in `ISessionStore`:

```csharp
Task<Session> MutateAsync(string sessionId, Action<Session> mutation, CancellationToken ct);
```

`RedisSessionStore` implements it as the CAS loop
([03-redis-schema.md](03-redis-schema.md#concurrency)): read, apply
`mutation` (domain method - throws on rule violations before anything is
written), bump version, EVALSHA, retry on conflict. Callers never touch
Redis semantics; domain rules never know about Redis.

## Infrastructure project

| Type | Notes |
| --- | --- |
| `RedisSessionStore : ISessionStore` | StackExchange.Redis; CAS Lua script loaded once at startup |
| `MinioReceiptStorage : IReceiptStorage` | AWS S3 SDK or Minio SDK against the `bill-splitter` bucket |
| `HttpOcrClient : IOcrClient` | typed `HttpClient` (named registration, 60s timeout) |
| `MailKitEmailSender : IEmailSender` | SMTP; summary template is an embedded resource rendered with string interpolation - no template engine |
| `OcrWorker : BackgroundService` | consumes `Channel<OcrJob>`; max 2 concurrent ([06-ocr-service.md](06-ocr-service.md#backend-job-flow)) |
| `OcrQueue` | wraps the bounded channel; `EnqueueAsync` returns false when full -> `429` at the controller |

## Api project

```
BillSplitter.Api/
  Controllers/
    SessionsController.cs      # create, get, receipt, open, finalize
    ItemsController.cs         # item CRUD + bill (review gate)
    ParticipantsController.cs  # join, rename me
    CodesController.cs         # short-code resolve
  Hubs/
    SessionHub.cs              # ClaimItem / UnclaimItem / SetShares
  Dtos/                        # request/response records + SnapshotMapper
  Auth/
    ParticipantTokenHandler.cs # authentication handler: bearer token -> participant claim
  Middleware/
    DomainExceptionMiddleware.cs # DomainException -> ProblemDetails
  Program.cs
```

- **Auth**: a custom `AuthenticationHandler` resolves
  `{sessionId from route/query} + bearer token -> participantId`, exposed
  as claims; `[Authorize]` + a `HostOnly` policy give per-endpoint control.
  The hub uses the same handler via `access_token`.
- **SnapshotMapper**: the only place `Session` becomes
  `SessionSnapshotDto`; calls `SplitCalculator` for computed fields. Every
  snapshot in the system flows through this one class.
- **Rate limiting**: built-in ASP.NET rate limiter, policies per
  [10-security-privacy.md](10-security-privacy.md#rate-limits).
- **OpenAPI**: Swashbuckle in dev; NSwag generates the frontend client in
  CI ([12-ci.md](12-ci.md)).

## Configuration

Options pattern, one class per concern, bound from `appsettings.json` +
env vars, validated with `ValidateDataAnnotations().ValidateOnStart()` -
missing required config kills startup, loudly.

| Options class | Keys (env form) | Default |
| --- | --- | --- |
| `AppOptions` | `App__PublicBaseUrl` | required |
| `RedisOptions` | `Redis__ConnectionString` | required |
| `MinioOptions` | `Minio__Endpoint`, `__AccessKey`, `__SecretKey`, `__Bucket` | bucket `bill-splitter` |
| `OcrOptions` | `Ocr__BaseUrl`, `__TimeoutSeconds`, `__MaxConcurrency`, `__QueueCapacity` | 60 / 2 / 16 |
| `SmtpOptions` | `Smtp__Host`, `__Port`, `__Username`, `__Password`, `__From` | required only if email enabled |
| `SessionOptions` | `Session__TtlHours`, `__FinalizedTtlMinutes`, `__MaxParticipants`, `__MaxItems`, `__MaxUploadBytes` | 24 / 60 / 20 / 100 / 10485760 |

Secrets (Redis/MinIO/SMTP credentials) come from environment only - never
committed ([13-deployment.md](13-deployment.md#environment)).

## Program.cs wiring (order matters)

1. Options binding + validation
2. `AddSignalR()`, controllers, ProblemDetails, rate limiter
3. Singletons: multiplexer, `RedisSessionStore`, `MinioReceiptStorage`,
   `OcrQueue`, `IIdGenerator`; typed `HttpClient` for OCR; scoped
   `SnapshotMapper`
4. `AddHostedService<OcrWorker>()`
5. Authentication (participant token handler) + `HostOnly` policy
6. Pipeline: exception middleware -> rate limiter -> auth -> controllers +
   `MapHub<SessionHub>("/hubs/session")` + `/healthz`

No CORS in production (SPA is served from the same origin,
[13-deployment.md](13-deployment.md)); permissive CORS for the Vite dev
server origin in Development only.
