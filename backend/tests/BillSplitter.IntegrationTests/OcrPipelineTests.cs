using System.Net;
using System.Net.Http.Json;
using BillSplitter.Domain.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace BillSplitter.IntegrationTests;

/// <summary>
/// The OCR pipeline over real HTTP: the sidecar failure path and the queue
/// backpressure (docs/11-testing-strategy.md#backend-integration, M3 A3/A4). The
/// sidecar is the WireMock stub on the factory, re-stubbed per test and restored
/// afterwards.
/// </summary>
[Collection(SessionApiCollection.Name)]
public sealed class OcrPipelineTests(SessionApiFactory factory)
{
    [Fact]
    public async Task Sidecar_error_lands_review_failed_and_stays_usable()
    {
        factory.Ocr.Reset();
        factory.Ocr.Given(Request.Create().WithPath("/ocr").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500).WithBodyAsJson(new { detail = "inference error" }));
        try
        {
            var created = await factory.PostCreateAsync();
            await factory.WaitForStateAsync(created.SessionId, "Review");

            using var client = factory.CreateClient();
            var snapshot = await client.GetFromJsonAsync<OcrStateView>(
                $"/api/v1/sessions/{created.SessionId}");

            snapshot!.State.Should().Be("Review");
            snapshot.Ocr.Status.Should().Be("Failed");
            snapshot.Ocr.FailureReason.Should().NotBeNullOrEmpty();

            // Manual entry still works: the review session takes items by hand.
            // (Item endpoints are M4; drive the aggregate through the store here.)
            using var scope = factory.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<ISessionStore>();
            var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
            await store.MutateAsync(
                created.SessionId, s => s.AddItem(ids.NewId(), "Margherita", 1, 1250, 100), CancellationToken.None);

            var updated = await client.GetFromJsonAsync<OcrStateView>(
                $"/api/v1/sessions/{created.SessionId}");
            updated!.Items.Should().ContainSingle(i => i.Name == "Margherita" && i.PriceMinor == 1250);
        }
        finally
        {
            factory.StubOcrEmpty();
        }
    }

    [Fact]
    public async Task Burst_of_19_uploads_keeps_two_in_flight_and_429s_the_overflow()
    {
        // Hold the sidecar so the two workers stay busy while the 16-slot queue
        // fills; the 19th upload has nowhere to go and must 429.
        factory.Ocr.Reset();
        factory.Ocr.Given(Request.Create().WithPath("/ocr").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBodyAsJson(new { durationMs = 1, lines = Array.Empty<object>() })
                .WithDelay(TimeSpan.FromSeconds(5)));

        var accepted = new List<string>();
        try
        {
            var results = await Task.WhenAll(
                Enumerable.Range(0, 19).Select(_ => factory.TryCreateAsync()));
            accepted.AddRange(results.Where(r => r.SessionId is not null).Select(r => r.SessionId!));
            var rejected = results.Count(r => r.Status == HttpStatusCode.TooManyRequests);

            // 2 in flight + 16 queued = 18 accepted at most; the rest overflow.
            accepted.Should().HaveCountLessThanOrEqualTo(18);
            rejected.Should().BeGreaterThanOrEqualTo(1);
            (accepted.Count + rejected).Should().Be(19);
            results.Select(r => r.Status).Where(s => s != HttpStatusCode.Accepted)
                .Should().OnlyContain(s => s == HttpStatusCode.TooManyRequests);
        }
        finally
        {
            // Drain the shared queue before yielding: restore the fast stub and let
            // every accepted job finish, so later tests do not see a full queue.
            factory.StubOcrEmpty();
            foreach (var sessionId in accepted)
            {
                await factory.WaitForStateAsync(sessionId, "Review", TimeSpan.FromSeconds(60));
            }
        }
    }

    private sealed record OcrStateView(string State, OcrView Ocr, List<ItemView> Items);

    private sealed record OcrView(string Status, string? FailureReason);

    private sealed record ItemView(string Name, long PriceMinor);
}
