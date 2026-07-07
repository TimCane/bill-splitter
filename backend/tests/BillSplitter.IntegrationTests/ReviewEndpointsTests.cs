using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BillSplitter.Domain.Sessions;
using FluentAssertions;

namespace BillSplitter.IntegrationTests;

/// <summary>Item CRUD + bill endpoints: host-only, Review-only, ISO currency
/// validation (docs/04-api-contract.md, M4 A1/A4).</summary>
[Collection(SessionApiCollection.Name)]
public sealed class ReviewEndpointsTests(SessionApiFactory factory)
{
    [Fact]
    public async Task Host_can_add_update_and_delete_an_item()
    {
        var session = await factory.CreateSessionAsync();

        var afterAdd = await SendAsync<ItemsView>(
            HttpMethod.Post, $"/api/v1/sessions/{session.SessionId}/items", session.ParticipantToken,
            new { name = "Margherita", quantity = 1, priceMinor = 1250 }, HttpStatusCode.Created);
        afterAdd.Items.Should().ContainSingle(i => i.Name == "Margherita" && i.PriceMinor == 1250);
        var itemId = afterAdd.Items[0].ItemId;

        var afterUpdate = await SendAsync<ItemsView>(
            HttpMethod.Put, $"/api/v1/sessions/{session.SessionId}/items/{itemId}", session.ParticipantToken,
            new { name = "Margherita pizza", quantity = 2, priceMinor = 2500 }, HttpStatusCode.OK);
        afterUpdate.Items.Should().ContainSingle(i => i.Name == "Margherita pizza" && i.Quantity == 2);

        var afterDelete = await SendAsync<ItemsView>(
            HttpMethod.Delete, $"/api/v1/sessions/{session.SessionId}/items/{itemId}", session.ParticipantToken,
            body: null, HttpStatusCode.OK);
        afterDelete.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Host_can_set_the_bill_and_the_checksum_is_computed()
    {
        var session = await factory.CreateSessionAsync();
        await SendAsync<ItemsView>(
            HttpMethod.Post, $"/api/v1/sessions/{session.SessionId}/items", session.ParticipantToken,
            new { name = "Beer", quantity = 1, priceMinor = 5000 }, HttpStatusCode.Created);

        var snapshot = await SendAsync<BillOnlyView>(
            HttpMethod.Put, $"/api/v1/sessions/{session.SessionId}/bill", session.ParticipantToken,
            new { taxMinor = 0, tipMinor = 500, serviceMinor = 0, totalMinor = 5450, currency = "GBP" },
            HttpStatusCode.OK);

        snapshot.Currency.Should().Be("GBP");
        snapshot.Bill.SubtotalMinor.Should().Be(5000);
        // subtotal 5000 + tip 500 - total 5450 = 50
        snapshot.Bill.ChecksumMinor.Should().Be(50);
    }

    [Theory]
    [InlineData("XYZ")]
    [InlineData("gbp")]
    [InlineData("GB")]
    public async Task Bill_with_a_non_iso_currency_is_rejected(string currency)
    {
        var session = await factory.CreateSessionAsync();

        var response = await SendRawAsync(
            HttpMethod.Put, $"/api/v1/sessions/{session.SessionId}/bill", session.ParticipantToken,
            new { taxMinor = 0, tipMinor = 0, serviceMinor = 0, totalMinor = 0, currency });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemView>();
        problem!.Type.Should().Be("validation");
    }

    [Fact]
    public async Task Non_host_cannot_edit_items()
    {
        var session = await factory.CreateSessionAsync();
        await factory.OpenAsync(session.SessionId, session.ParticipantId);

        using var client = factory.CreateClient();
        var join = (await (await client.PostAsJsonAsync(
            $"/api/v1/sessions/{session.SessionId}/participants", new { displayName = "Sam" }))
            .Content.ReadFromJsonAsync<JoinView>())!;

        var response = await SendRawAsync(
            HttpMethod.Post, $"/api/v1/sessions/{session.SessionId}/items", join.ParticipantToken,
            new { name = "Sneaky", quantity = 1, priceMinor = 100 });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await response.Content.ReadFromJsonAsync<ProblemView>();
        problem!.Type.Should().Be("not-host");
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method, string uri, string token, object? body, HttpStatusCode expected)
    {
        var response = await SendRawAsync(method, uri, token, body);
        response.StatusCode.Should().Be(expected);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<HttpResponseMessage> SendRawAsync(
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

public sealed record ItemsView(List<ItemView> Items);

public sealed record ItemView(string ItemId, string Name, int Quantity, long PriceMinor);

public sealed record BillOnlyView(string Currency, BillView Bill);

public sealed record BillView(long SubtotalMinor, long TotalMinor, long ChecksumMinor);
