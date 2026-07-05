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

namespace BillSplitter.IntegrationTests;

/// <summary>
/// Hosts the API against real Redis and MinIO containers (docs/11-testing-strategy.md#backend-integration).
/// OCR is faked instant-empty by the create endpoint in M2, so no sidecar stub is needed.
/// </summary>
public sealed class SessionApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder().WithImage("redis:7-alpine").Build();
    private readonly MinioContainer _minio = new MinioBuilder().Build();

    public string RedisConnectionString => _redis.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();
        await _minio.StartAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _redis.DisposeAsync();
        await _minio.DisposeAsync();
    }

    public IConnectionMultiplexer ConnectRedis() => ConnectionMultiplexer.Connect(RedisConnectionString);

    /// <summary>Create a session (Review, empty) via the real endpoint.</summary>
    public async Task<CreatedSession> CreateSessionAsync()
    {
        using var client = CreateClient();
        using var form = new MultipartFormDataContent();
        var jpeg = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10]);
        jpeg.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(jpeg, "image", "receipt.jpg");

        var response = await client.PostAsync("/api/v1/sessions", form);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatedSession>())!;
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
            ["Ocr:BaseUrl"] = "http://localhost:9",
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
