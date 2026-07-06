using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BillSplitter.Domain;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BillSplitter.IntegrationTests;

/// <summary>Receipt image endpoint: host-only, echoed content-type, 404 once the
/// stored object is gone (docs/04-api-contract.md#get-apiv1sessionssessionidreceipt,
/// M4 A3/A4).</summary>
[Collection(SessionApiCollection.Name)]
public sealed class ReceiptEndpointTests(SessionApiFactory factory)
{
    [Fact]
    public async Task Host_gets_the_stored_image_with_its_content_type()
    {
        var session = await factory.CreateSessionAsync();

        var response = await GetReceiptAsync(session.SessionId, session.ParticipantToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
        (await response.Content.ReadAsByteArrayAsync()).Should().NotBeEmpty();
    }

    [Fact]
    public async Task Receipt_is_not_found_once_the_object_is_deleted()
    {
        var session = await factory.CreateSessionAsync();

        using (var scope = factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IReceiptStorage>();
            await storage.DeleteAsync(session.SessionId, CancellationToken.None);
        }

        var response = await GetReceiptAsync(session.SessionId, session.ParticipantToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<ProblemView>();
        problem!.Type.Should().Be("receipt-not-found");
    }

    [Fact]
    public async Task Non_host_cannot_read_the_receipt()
    {
        var session = await factory.CreateSessionAsync();
        await factory.OpenAsync(session.SessionId, session.ParticipantId);

        using var client = factory.CreateClient();
        var join = (await (await client.PostAsJsonAsync(
            $"/api/v1/sessions/{session.SessionId}/participants", new { displayName = "Sam" }))
            .Content.ReadFromJsonAsync<JoinView>())!;

        var response = await GetReceiptAsync(session.SessionId, join.ParticipantToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await response.Content.ReadFromJsonAsync<ProblemView>();
        problem!.Type.Should().Be("not-host");
    }

    [Fact]
    public async Task Receipt_without_a_token_is_missing_token()
    {
        var session = await factory.CreateSessionAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/sessions/{session.SessionId}/receipt");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpResponseMessage> GetReceiptAsync(string sessionId, string token)
    {
        using var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/sessions/{sessionId}/receipt");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}
