using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;

namespace BillSplitter.IntegrationTests;

/// <summary>Auth matrix over real HTTP (docs/11-testing-strategy.md#backend-integration).
/// Non-host coverage lands in M4 with the first host-only endpoint; M2 exposes none.</summary>
[Collection(SessionApiCollection.Name)]
public sealed class AuthMatrixTests(SessionApiFactory factory)
{
    [Fact]
    public async Task Rename_with_wrong_token_is_forbidden_unknown_participant()
    {
        var session = await factory.CreateSessionAsync();
        using var client = factory.CreateClient();

        var request = new HttpRequestMessage(
            HttpMethod.Put, $"/api/v1/sessions/{session.SessionId}/participants/me")
        {
            Content = JsonContent.Create(new { displayName = "Mallory" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-token");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await response.Content.ReadFromJsonAsync<ProblemView>();
        problem!.Type.Should().Be("unknown-participant");
    }

    [Fact]
    public async Task Rename_without_token_is_missing_token()
    {
        var session = await factory.CreateSessionAsync();
        using var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/v1/sessions/{session.SessionId}/participants/me", new { displayName = "Tim" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var problem = await response.Content.ReadFromJsonAsync<ProblemView>();
        problem!.Type.Should().Be("missing-token");
    }

    [Fact]
    public async Task Join_when_not_open_is_wrong_state()
    {
        // A freshly created session is in Review, not Open.
        var session = await factory.CreateSessionAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/sessions/{session.SessionId}/participants", new { displayName = "Sam" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<ProblemView>();
        problem!.Type.Should().Be("wrong-state");
    }
}
