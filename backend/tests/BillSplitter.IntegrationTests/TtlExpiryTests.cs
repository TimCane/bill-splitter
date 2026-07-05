using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace BillSplitter.IntegrationTests;

/// <summary>TTL is set at create and an expired session reads as 404
/// (docs/14-build-order.md#m2---session-core, A3).</summary>
[Collection(SessionApiCollection.Name)]
public sealed class TtlExpiryTests(SessionApiFactory factory)
{
    [Fact]
    public async Task Create_sets_a_24h_ttl_and_expiry_yields_404()
    {
        var session = await factory.CreateSessionAsync();
        var key = $"session:{session.SessionId}";

        using var mux = factory.ConnectRedis();
        var db = mux.GetDatabase();

        var ttl = await db.KeyTimeToLiveAsync(key);
        ttl.Should().NotBeNull();
        ttl!.Value.Should().BeGreaterThan(TimeSpan.FromHours(23)).And.BeLessThanOrEqualTo(TimeSpan.FromHours(24));

        // Force expiry rather than waiting 24h.
        await db.KeyExpireAsync(key, TimeSpan.FromSeconds(1));
        await Task.Delay(TimeSpan.FromMilliseconds(1500));

        using var client = factory.CreateClient();
        var response = await client.GetAsync($"/api/v1/sessions/{session.SessionId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<ProblemView>();
        problem!.Type.Should().Be("session-not-found");
    }
}
