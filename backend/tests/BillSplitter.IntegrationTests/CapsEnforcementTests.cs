using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using FluentAssertions;

namespace BillSplitter.IntegrationTests;

/// <summary>
/// Session-level caps are enforced at the surfaces that mutate a session
/// (docs/10-security-privacy.md#session-level-nuisance-controls, M7 A5): name
/// length on join, the participant cap, and the item cap. Shares (1-99) funnel
/// through the same aggregate method and are covered by the domain suite. Tight
/// caps are bound per-host so the burst stays cheap.
/// </summary>
[Collection(SessionApiCollection.Name)]
public sealed class CapsEnforcementTests(SessionApiFactory factory)
{
    private WebApplicationFactory<Program> TightCaps() =>
        factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Session:MaxParticipants"] = "2",
                    ["Session:MaxItems"] = "1",
                })));

    [Fact]
    public async Task A_display_name_over_30_chars_is_rejected()
    {
        var session = await factory.CreateSessionAsync();
        await factory.OpenAsync(session.SessionId, session.ParticipantId);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/sessions/{session.SessionId}/participants",
            new { displayName = new string('x', 31) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemView>())!.Type.Should().Be("validation");
    }

    [Fact]
    public async Task Joining_past_the_participant_cap_is_rejected()
    {
        using var host = TightCaps();
        var session = await factory.CreateSessionAsync();
        await factory.OpenAsync(session.SessionId, session.ParticipantId);
        using var client = host.CreateClient();

        // Host already fills one of the two seats; one joiner fits, the next does not.
        var first = await client.PostAsJsonAsync(
            $"/api/v1/sessions/{session.SessionId}/participants", new { displayName = "Fits" });
        var second = await client.PostAsJsonAsync(
            $"/api/v1/sessions/{session.SessionId}/participants", new { displayName = "Overflow" });

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await second.Content.ReadFromJsonAsync<ProblemView>())!.Type.Should().Be("session-full");
    }

    [Fact]
    public async Task Adding_past_the_item_cap_is_rejected()
    {
        using var host = TightCaps();
        var session = await factory.CreateSessionAsync();
        using var client = host.CreateClient();

        var first = await AddItemAsync(client, session, "Coffee");
        var second = await AddItemAsync(client, session, "Cake");

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await second.Content.ReadFromJsonAsync<ProblemView>())!.Type.Should().Be("validation");
    }

    private static async Task<HttpResponseMessage> AddItemAsync(
        HttpClient client, CreatedSession session, string name)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sessions/{session.SessionId}/items")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", session.ParticipantToken) },
            Content = JsonContent.Create(new { name, quantity = 1, priceMinor = 500 }),
        };
        return await client.SendAsync(request);
    }
}
