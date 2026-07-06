using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BillSplitter.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>Post the create form and return just the status - for the overflow
    /// burst where some uploads are expected to 429.</summary>
    public async Task<HttpStatusCode> TryCreateAsync()
    {
        using var client = CreateClient();
        using var form = ImageForm();
        var response = await client.PostAsync("/api/v1/sessions", form);
        return response.StatusCode;
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
        }));
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
