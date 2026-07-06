using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;

namespace BillSplitter.IntegrationTests;

/// <summary>POST /finalize: host-only, Open-only, and it shrinks the session and
/// code keys to ~1h in the same commit (docs/04-api-contract.md, M6 A4).</summary>
[Collection(SessionApiCollection.Name)]
public sealed class FinalizeEndpointTests(SessionApiFactory factory)
{
    [Fact]
    public async Task Host_finalize_locks_the_split_and_shrinks_both_keys_to_an_hour()
    {
        var session = await factory.CreateSessionAsync();
        var open = (await (await OpenAsync(session.SessionId, session.ParticipantToken))
            .Content.ReadFromJsonAsync<OpenView>())!;

        var response = await FinalizeAsync(session.SessionId, session.ParticipantToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var snapshot = (await response.Content.ReadFromJsonAsync<SnapshotView>())!;
        snapshot.State.Should().Be("Finalized");

        using var mux = factory.ConnectRedis();
        var db = mux.GetDatabase();
        var sessionTtl = await db.KeyTimeToLiveAsync($"session:{session.SessionId}");
        var codeTtl = await db.KeyTimeToLiveAsync($"code:{open.ShortCode}");

        sessionTtl.Should().NotBeNull();
        codeTtl.Should().NotBeNull();
        sessionTtl!.Value.Should().BeGreaterThan(TimeSpan.FromMinutes(58)).And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(60));
        codeTtl!.Value.Should().BeGreaterThan(TimeSpan.FromMinutes(58)).And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(60));
    }

    [Fact]
    public async Task Finalize_before_open_is_wrong_state()
    {
        var session = await factory.CreateSessionAsync();

        var response = await FinalizeAsync(session.SessionId, session.ParticipantToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<ProblemView>();
        problem!.Type.Should().Be("wrong-state");
    }

    [Fact]
    public async Task Non_host_cannot_finalize()
    {
        var session = await factory.CreateSessionAsync();
        (await OpenAsync(session.SessionId, session.ParticipantToken)).EnsureSuccessStatusCode();

        using var client = factory.CreateClient();
        var join = (await (await client.PostAsJsonAsync(
            $"/api/v1/sessions/{session.SessionId}/participants", new { displayName = "Sam" }))
            .Content.ReadFromJsonAsync<JoinView>())!;

        var response = await FinalizeAsync(session.SessionId, join.ParticipantToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await response.Content.ReadFromJsonAsync<ProblemView>();
        problem!.Type.Should().Be("not-host");
    }

    private Task<HttpResponseMessage> OpenAsync(string sessionId, string token) =>
        SendAsync(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/open", token, body: null);

    private Task<HttpResponseMessage> FinalizeAsync(string sessionId, string token) =>
        SendAsync(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/finalize", token, body: new { });

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string uri, string token, object? body)
    {
        using var client = factory.CreateClient();
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }
}
