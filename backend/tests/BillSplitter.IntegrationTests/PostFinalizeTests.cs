using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BillSplitter.IntegrationTests;

/// <summary>The finalize guarantees that need a live stack: the whole bill is paid
/// after finalize (A1, end to end) and the summary address touches neither Redis
/// nor the logs (A3) (docs/02-domain-model.md#invariants-property-test-these,
/// docs/10-security-privacy.md#ephemerality-guarantees).</summary>
[Collection(SessionApiCollection.Name)]
public sealed class PostFinalizeTests(SessionApiFactory factory)
{
    [Fact]
    public async Task Finalized_per_person_totals_sum_to_the_whole_bill()
    {
        var session = await factory.CreateSessionAsync();

        // A priced item and extras, nothing claimed: finalize splits the lot.
        await SendAsync(HttpMethod.Post, $"/api/v1/sessions/{session.SessionId}/items",
            session.ParticipantToken, new { name = "Platter", quantity = 1, priceMinor = 5000 });
        await SendAsync(HttpMethod.Put, $"/api/v1/sessions/{session.SessionId}/bill",
            session.ParticipantToken,
            new { taxMinor = 125, tipMinor = 500, serviceMinor = 0, totalMinor = 5625, currency = "GBP" });
        (await SendAsync(HttpMethod.Post, $"/api/v1/sessions/{session.SessionId}/open",
            session.ParticipantToken, body: null)).EnsureSuccessStatusCode();

        using var client = factory.CreateClient();
        await client.PostAsJsonAsync(
            $"/api/v1/sessions/{session.SessionId}/participants", new { displayName = "Sam" });

        var response = await SendAsync(HttpMethod.Post,
            $"/api/v1/sessions/{session.SessionId}/finalize", session.ParticipantToken, new { });
        var snapshot = (await response.Content.ReadFromJsonAsync<FinalizedView>())!;

        snapshot.State.Should().Be("Finalized");
        var wholeBill = snapshot.Bill.SubtotalMinor + snapshot.Bill.TaxMinor
            + snapshot.Bill.TipMinor + snapshot.Bill.ServiceMinor;
        snapshot.Totals.Sum(t => t.TotalMinor).Should().Be(wholeBill);
    }

    [Fact]
    public async Task Summary_address_never_reaches_redis_or_the_logs()
    {
        const string address = "diner-secret@example.com";

        var logs = new CapturingLoggerProvider();
        using var configured = factory.WithWebHostBuilder(builder =>
        {
            // A configured but unreachable relay: the send runs and fails, so the
            // sender's logging discipline is what is under test, not delivery.
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Smtp:Host"] = "127.0.0.1",
                    ["Smtp:Port"] = "9",
                    ["Smtp:From"] = "noreply@bill.example",
                }));
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
        });

        // The base and configured hosts share the same Redis and OCR containers, so
        // set the session up on the base factory and finalize through the SMTP-enabled
        // host, whose sender and logger are the ones under test.
        var session = await CreateOpenSessionAsync();
        var response = await SendAsync(configured, HttpMethod.Post,
            $"/api/v1/sessions/{session.SessionId}/finalize", session.ParticipantToken,
            new { email = address });
        response.EnsureSuccessStatusCode();

        // The send is fire-and-forget; wait for the failure it must log.
        await WaitForLogAsync(logs, "summary email failed");

        logs.Messages.Should().NotContain(m => m.Contains(address, StringComparison.OrdinalIgnoreCase));

        using var mux = factory.ConnectRedis();
        var server = mux.GetServer(mux.GetEndPoints().Single());
        var db = mux.GetDatabase();
        foreach (var key in server.Keys(pattern: "*"))
        {
            var value = await db.StringGetAsync(key);
            if (!value.IsNull)
            {
                value.ToString().Should().NotContain(address);
            }
        }
    }

    private async Task<CreatedSession> CreateOpenSessionAsync()
    {
        var created = await factory.CreateSessionAsync();
        (await SendAsync(HttpMethod.Post,
            $"/api/v1/sessions/{created.SessionId}/open", created.ParticipantToken, body: null))
            .EnsureSuccessStatusCode();
        return created;
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string uri, string token, object? body) =>
        SendAsync(factory, method, uri, token, body);

    private static async Task<HttpResponseMessage> SendAsync(
        WebApplicationFactory<Program> factory, HttpMethod method, string uri, string token, object? body)
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

    private static async Task WaitForLogAsync(CapturingLoggerProvider logs, string fragment)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (logs.Messages.Any(m => m.Contains(fragment, StringComparison.Ordinal)))
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"no log line containing '{fragment}' was emitted");
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) => sink.Enqueue(formatter(state, exception));
        }
    }
}

public sealed record FinalizedView(string State, FinalizeBillView Bill, List<FinalizeTotalView> Totals);

public sealed record FinalizeBillView(long SubtotalMinor, long TaxMinor, long TipMinor, long ServiceMinor);

public sealed record FinalizeTotalView(long TotalMinor);
