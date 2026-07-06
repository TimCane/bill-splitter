using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;

namespace BillSplitter.IntegrationTests;

/// <summary>GET /codes/{shortCode}: anonymous resolve to a session id, 404 on an
/// unknown code (docs/04-api-contract.md#get-apiv1codesshortcode, M4 A3).</summary>
[Collection(SessionApiCollection.Name)]
public sealed class CodeResolveTests(SessionApiFactory factory)
{
    [Fact]
    public async Task A_minted_code_resolves_anonymously_to_its_session()
    {
        var session = await factory.CreateSessionAsync();
        var open = await OpenAsync(session.SessionId, session.ParticipantToken);
        var shortCode = (await open.Content.ReadFromJsonAsync<OpenView>())!.ShortCode;

        using var client = factory.CreateClient();
        var response = await client.GetAsync($"/api/v1/codes/{shortCode}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ResolveView>();
        body!.SessionId.Should().Be(session.SessionId);
    }

    [Fact]
    public async Task An_unknown_code_is_not_found()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/codes/ZZZZZZ");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<ProblemView>();
        problem!.Type.Should().Be("session-not-found");
    }

    private async Task<HttpResponseMessage> OpenAsync(string sessionId, string token)
    {
        using var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/open");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}

public sealed record ResolveView(string SessionId);
