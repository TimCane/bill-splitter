using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BillSplitter.Domain;
using BillSplitter.Infrastructure.Redis;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Testcontainers.Minio;
using Testcontainers.Redis;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace BillSplitter.IntegrationTests;

/// <summary>
/// Hosts the API against real Redis and MinIO containers, with the OCR sidecar
/// replaced by a WireMock stub returning fixture JSON - the real sidecar is too
/// heavy for CI (docs/11-testing-strategy.md#backend-integration). The default
/// stub returns an empty parse, so a freshly created session lands in an empty
/// <c>Review</c>; a test can re-stub the sidecar to drive failure or backpressure.
/// </summary>
public sealed class SessionApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder().WithImage("redis:7-alpine").Build();
    private readonly MinioContainer _minio = new MinioBuilder().Build();
    private WireMockServer _ocr = null!;
    private int _casConflicts;

    /// <summary>Count of CAS conflict retries logged by the session store since
    /// startup - how the concurrency tests observe that writers really raced
    /// (docs/11-testing-strategy.md#backend-integration).</summary>
    public int CasConflictCount => Volatile.Read(ref _casConflicts);

    public string RedisConnectionString => _redis.GetConnectionString();

    /// <summary>The stub standing in for the OCR sidecar. Reset with
    /// <see cref="StubOcrEmpty"/>; override per test for failure/backpressure.</summary>
    public WireMockServer Ocr => _ocr;

    public async Task InitializeAsync()
    {
        _ocr = WireMockServer.Start();
        StubOcrEmpty();
        await _redis.StartAsync();
        await _minio.StartAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        _ocr.Stop();
        await _redis.DisposeAsync();
        await _minio.DisposeAsync();
    }

    public IConnectionMultiplexer ConnectRedis() => ConnectionMultiplexer.Connect(RedisConnectionString);

    /// <summary>Default stub: a valid, empty OCR result plus a healthy probe.</summary>
    public void StubOcrEmpty()
    {
        _ocr.Reset();
        _ocr.Given(Request.Create().WithPath("/healthz").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { status = "ok" }));
        _ocr.Given(Request.Create().WithPath("/ocr").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBodyAsJson(new { durationMs = 1, lines = Array.Empty<object>() }));
    }

    /// <summary>Create a session and wait for the OCR worker to land it in Review.</summary>
    public async Task<CreatedSession> CreateSessionAsync()
    {
        var created = await PostCreateAsync();
        await WaitForStateAsync(created.SessionId, "Review");
        return created;
    }

    /// <summary>Post the create form without waiting - for backpressure tests that
    /// fire many uploads at once.</summary>
    public async Task<CreatedSession> PostCreateAsync()
    {
        using var client = CreateClient();
        using var form = ImageForm();
        var response = await client.PostAsync("/api/v1/sessions", form);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatedSession>())!;
    }

    /// <summary>Post the create form and return the status plus the new session id
    /// (null when rejected) - for the overflow burst where some uploads 429.</summary>
    public async Task<(HttpStatusCode Status, string? SessionId)> TryCreateAsync()
    {
        using var client = CreateClient();
        using var form = ImageForm();
        var response = await client.PostAsync("/api/v1/sessions", form);
        if (response.StatusCode != HttpStatusCode.Accepted)
        {
            return (response.StatusCode, null);
        }

        var created = (await response.Content.ReadFromJsonAsync<CreatedSession>())!;
        return (response.StatusCode, created.SessionId);
    }

    private static MultipartFormDataContent ImageForm()
    {
        var form = new MultipartFormDataContent();
        var jpeg = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10]);
        jpeg.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(jpeg, "image", "receipt.jpg");
        return form;
    }

    public async Task WaitForStateAsync(string sessionId, string state, TimeSpan? timeout = null)
    {
        using var client = CreateClient();
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (DateTimeOffset.UtcNow < deadline)
        {
            var response = await client.GetAsync($"/api/v1/sessions/{sessionId}");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var snapshot = (await response.Content.ReadFromJsonAsync<SnapshotView>())!;
                if (snapshot.State == state)
                {
                    return;
                }
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"session {sessionId} did not reach state {state}");
    }

    /// <summary>Open a hub connection as the given participant. The in-memory
    /// TestServer has no real sockets, so the transport is long polling over the
    /// server's handler. The token goes through AccessTokenProvider - on long
    /// polling that is a bearer header, the same path the browser client falls
    /// back to when WebSockets are blocked.</summary>
    public async Task<HubConnection> ConnectHubAsync(string sessionId, string participantToken)
    {
        var url = new Uri(
            Server.BaseAddress,
            $"/hubs/session?sessionId={Uri.EscapeDataString(sessionId)}");
        var connection = new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                options.HttpMessageHandlerFactory = _ => Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(participantToken);
            })
            .Build();
        await connection.StartAsync();
        return connection;
    }

    /// <summary>Advance a session to Open through the store (the open endpoint is M4).</summary>
    public async Task OpenAsync(string sessionId, string hostParticipantId)
    {
        using var scope = Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISessionStore>();
        await store.MutateAsync(sessionId, s => s.Open(hostParticipantId, "K7MPQ2"), CancellationToken.None);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var hostPort = _minio.GetConnectionString()
            .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase);

        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["App:PublicBaseUrl"] = "http://localhost",
            ["Redis:ConnectionString"] = RedisConnectionString,
            ["Minio:Endpoint"] = $"http://{hostPort}",
            ["Minio:AccessKey"] = _minio.GetAccessKey(),
            ["Minio:SecretKey"] = _minio.GetSecretKey(),
            ["Minio:Bucket"] = "bill-splitter",
            ["Ocr:BaseUrl"] = _ocr.Url,
            // The concurrency tests hammer gestures far past the production
            // 10/sec throttle; the throttle itself is unit-scoped, not under test.
            ["Session:HubGesturesPerSecond"] = "10000",
        }));
        builder.ConfigureLogging(logging =>
        {
            logging.AddProvider(new CasConflictCountingProvider(() => Interlocked.Increment(ref _casConflicts)));
            logging.AddFilter<CasConflictCountingProvider>(
                typeof(RedisSessionStore).FullName, LogLevel.Debug);
        });
    }

    /// <summary>Counts the session store's "CAS conflict" debug lines; everything
    /// else is filtered out before it reaches this provider.</summary>
    private sealed class CasConflictCountingProvider(Action increment) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CountingLogger(increment);

        public void Dispose()
        {
        }

        private sealed class CountingLogger(Action increment) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Debug;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (formatter(state, exception).StartsWith("CAS conflict", StringComparison.Ordinal))
                {
                    increment();
                }
            }
        }
    }
}

public sealed record CreatedSession(
    string SessionId,
    string ParticipantId,
    string ParticipantToken,
    string DisplayName);

public sealed record SnapshotView(string State, int Version, List<ParticipantView> Participants);

public sealed record ParticipantView(string ParticipantId, string DisplayName, bool IsHost);

public sealed record JoinView(string ParticipantId, string ParticipantToken, SnapshotView Snapshot);

public sealed record ProblemView(string Type, int Status);

[CollectionDefinition(Name)]
public sealed class SessionApiCollection : ICollectionFixture<SessionApiFactory>
{
    public const string Name = "session-api";
}
