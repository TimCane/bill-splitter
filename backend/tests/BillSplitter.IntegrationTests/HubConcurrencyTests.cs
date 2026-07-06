using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;

namespace BillSplitter.IntegrationTests;

/// <summary>Two hub clients hammering <c>SetShares</c> on one item must converge
/// on a consistent snapshot with no failed invocations, and the CAS funnel must
/// actually retry under the contention
/// (docs/11-testing-strategy.md#backend-integration, M5 A3).</summary>
[Collection(SessionApiCollection.Name)]
public sealed class HubConcurrencyTests(SessionApiFactory factory)
{
    private const int InvocationsPerClient = 100;

    [Fact]
    public async Task Parallel_SetShares_from_two_clients_converges_without_errors()
    {
        var session = await factory.CreateSessionAsync();
        var itemId = await AddItemAsync(session, "Peroni 660ml", priceMinor: 1100);
        await factory.OpenAsync(session.SessionId, session.ParticipantId);
        var joiner = await JoinAsync(session.SessionId, "Sam");

        await using var host = await factory.ConnectHubAsync(session.SessionId, session.ParticipantToken);
        await using var guest = await factory.ConnectHubAsync(session.SessionId, joiner.ParticipantToken);

        var latestHostVersion = 0;
        var latestGuestVersion = 0;
        host.On<JsonElement>("SnapshotUpdated", s =>
            Interlocked.Exchange(ref latestHostVersion, s.GetProperty("version").GetInt32()));
        guest.On<JsonElement>("SnapshotUpdated", s =>
            Interlocked.Exchange(ref latestGuestVersion, s.GetProperty("version").GetInt32()));

        var before = await GetSnapshotAsync(session.SessionId);
        var conflictsBefore = factory.CasConflictCount;

        // Both connections pipeline their gestures; SignalR dispatches each
        // connection's invocations in order, so the two writers interleave.
        var invocations = new List<Task>(InvocationsPerClient * 2);
        for (var i = 0; i < InvocationsPerClient; i++)
        {
            invocations.Add(host.InvokeAsync("SetShares", itemId, i % 9 + 1));
            invocations.Add(guest.InvokeAsync("SetShares", itemId, i % 9 + 1));
        }

        // No HubException surfaces - in particular no conflict-retry-exhausted.
        await Task.WhenAll(invocations);

        // Every gesture is one CAS commit: no lost or duplicated writes.
        var after = await GetSnapshotAsync(session.SessionId);
        after.Version.Should().Be(before.Version + InvocationsPerClient * 2);

        // The final claim set is consistent: one claim per writer at the last
        // weight each sent, allocations summing exactly to the item price.
        var item = after.Items.Should().ContainSingle(i => i.ItemId == itemId).Subject;
        var finalShares = (InvocationsPerClient - 1) % 9 + 1;
        item.Claims.Should().HaveCount(2);
        item.Claims.Select(c => c.ParticipantId)
            .Should().BeEquivalentTo([session.ParticipantId, joiner.ParticipantId]);
        item.Claims.Should().OnlyContain(c => c.Shares == finalShares);
        item.Claims.Sum(c => c.AllocatedMinor).Should().Be(item.PriceMinor);

        // Two writers racing one document: the CAS loop must have retried.
        factory.CasConflictCount.Should().BeGreaterThan(conflictsBefore);

        // The coalesced broadcast catches both clients up to the final version.
        await WaitForAsync(
            () => Volatile.Read(ref latestHostVersion) >= after.Version
                && Volatile.Read(ref latestGuestVersion) >= after.Version,
            TimeSpan.FromSeconds(5),
            "both clients receive the final coalesced snapshot");
    }

    private async Task<string> AddItemAsync(CreatedSession session, string name, long priceMinor)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/sessions/{session.SessionId}/items")
        {
            Content = JsonContent.Create(new { name, quantity = 1, priceMinor }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.ParticipantToken);

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var snapshot = (await response.Content.ReadFromJsonAsync<ClaimSnapshotView>())!;
        return snapshot.Items.Single().ItemId;
    }

    private async Task<JoinView> JoinAsync(string sessionId, string displayName)
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/sessions/{sessionId}/participants", new { displayName });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JoinView>())!;
    }

    private async Task<ClaimSnapshotView> GetSnapshotAsync(string sessionId)
    {
        using var client = factory.CreateClient();
        return (await client.GetFromJsonAsync<ClaimSnapshotView>($"/api/v1/sessions/{sessionId}"))!;
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout, string description)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        condition().Should().BeTrue(description);
    }

    private sealed record ClaimSnapshotView(int Version, List<ClaimItemView> Items);

    private sealed record ClaimItemView(string ItemId, long PriceMinor, List<ClaimView> Claims);

    private sealed record ClaimView(string ParticipantId, int Shares, long AllocatedMinor);
}
