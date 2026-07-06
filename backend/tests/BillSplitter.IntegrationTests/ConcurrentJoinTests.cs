using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace BillSplitter.IntegrationTests;

/// <summary>The CAS funnel must not lose writes under parallel joins
/// (docs/14-build-order.md#m2---session-core, A2).</summary>
[Collection(SessionApiCollection.Name)]
public sealed class ConcurrentJoinTests(SessionApiFactory factory)
{
    [Fact]
    public async Task Parallel_joins_never_corrupt_the_document()
    {
        const int joiners = 10;
        var session = await factory.CreateSessionAsync();
        await factory.OpenAsync(session.SessionId, session.ParticipantId);

        var responses = await Task.WhenAll(Enumerable.Range(0, joiners).Select(async i =>
        {
            using var client = factory.CreateClient();
            return await client.PostAsJsonAsync(
                $"/api/v1/sessions/{session.SessionId}/participants",
                new { displayName = $"Joiner {i}" });
        }));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);

        using var reader = factory.CreateClient();
        var snapshot = await reader.GetFromJsonAsync<SnapshotView>($"/api/v1/sessions/{session.SessionId}");

        // Host + every joiner survived; no lost updates.
        snapshot!.Participants.Should().HaveCount(joiners + 1);
        snapshot.Participants.Select(p => p.DisplayName)
            .Where(n => n.StartsWith("Joiner", StringComparison.Ordinal))
            .Distinct()
            .Should().HaveCount(joiners);
    }
}
