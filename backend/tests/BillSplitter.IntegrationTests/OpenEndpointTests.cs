using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BillSplitter.Domain;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BillSplitter.IntegrationTests;

/// <summary>POST /open: mint the code, delete the image, resolve the code
/// (docs/04-api-contract.md#post-apiv1sessionssessionidopen, M4 A3/A4).</summary>
[Collection(SessionApiCollection.Name)]
public sealed class OpenEndpointTests(SessionApiFactory factory)
{
    [Fact]
    public async Task Host_open_mints_a_code_deletes_the_image_and_resolves()
    {
        var session = await factory.CreateSessionAsync();

        var open = await OpenAsync(session.SessionId, session.ParticipantToken);
        open.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await open.Content.ReadFromJsonAsync<OpenView>())!;

        // 6 chars from the no-0/O/1/I/L alphabet.
        body.ShortCode.Should().MatchRegex("^[ABCDEFGHJKMNPQRSTUVWXYZ23456789]{6}$");
        body.JoinUrl.Should().Be($"http://localhost/s/{session.SessionId}");

        // The code key resolves back to this session.
        using (var scope = factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ISessionStore>();
            (await store.ResolveCodeAsync(body.ShortCode, CancellationToken.None)).Should().Be(session.SessionId);

            // The stored receipt object is gone.
            var storage = scope.ServiceProvider.GetRequiredService<IReceiptStorage>();
            (await storage.GetAsync(session.SessionId, CancellationToken.None)).Should().BeNull();
        }

        // ...and the receipt endpoint now 404s.
        var receipt = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/sessions/{session.SessionId}/receipt");
        receipt.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.ParticipantToken);
        using var client = factory.CreateClient();
        (await client.SendAsync(receipt)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Open_when_already_open_is_wrong_state()
    {
        var session = await factory.CreateSessionAsync();
        (await OpenAsync(session.SessionId, session.ParticipantToken)).EnsureSuccessStatusCode();

        var again = await OpenAsync(session.SessionId, session.ParticipantToken);

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await again.Content.ReadFromJsonAsync<ProblemView>();
        problem!.Type.Should().Be("wrong-state");
    }

    [Fact]
    public async Task Non_host_cannot_open()
    {
        var session = await factory.CreateSessionAsync();
        await factory.OpenAsync(session.SessionId, session.ParticipantId);

        using var client = factory.CreateClient();
        var join = (await (await client.PostAsJsonAsync(
            $"/api/v1/sessions/{session.SessionId}/participants", new { displayName = "Sam" }))
            .Content.ReadFromJsonAsync<JoinView>())!;

        var response = await OpenAsync(session.SessionId, join.ParticipantToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await response.Content.ReadFromJsonAsync<ProblemView>();
        problem!.Type.Should().Be("not-host");
    }

    private async Task<HttpResponseMessage> OpenAsync(string sessionId, string token)
    {
        using var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/open");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}

public sealed record OpenView(string ShortCode, string JoinUrl);
