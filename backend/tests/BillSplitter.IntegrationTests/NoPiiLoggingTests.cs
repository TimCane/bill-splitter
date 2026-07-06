using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using FluentAssertions;

namespace BillSplitter.IntegrationTests;

/// <summary>
/// The no-PII logging rule (docs/10-security-privacy.md#ephemerality-guarantees,
/// M7 A5): logs carry ids and states, never display names, email addresses or
/// image bytes. Drives a full flow whose name and email are unique canaries and
/// asserts neither ever reaches a log line.
/// </summary>
[Collection(SessionApiCollection.Name)]
public sealed class NoPiiLoggingTests(SessionApiFactory factory)
{
    private const string CanaryName = "Zaphod-Canary";
    private const string CanaryEmail = "canary-9f3@example.test";

    private static readonly byte[] TinyJpeg =
        [0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x64, 0x00, 0x64];

    [Fact]
    public async Task No_log_line_carries_a_display_name_or_email()
    {
        var logs = new ConcurrentQueue<string>();
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging =>
            {
                logging.AddProvider(new CapturingLoggerProvider(logs));
                logging.SetMinimumLevel(LogLevel.Trace);
            }));
        using var client = host.CreateClient();

        // Create -> Review (empty OCR stub) -> open -> join with the canary name.
        var created = await CreateAsync(client);
        await WaitForReviewAsync(client, created.SessionId);
        await PostAuthedAsync(client, $"/api/v1/sessions/{created.SessionId}/open", created.ParticipantToken);
        var join = await client.PostAsJsonAsync(
            $"/api/v1/sessions/{created.SessionId}/participants", new { displayName = CanaryName });
        join.StatusCode.Should().Be(HttpStatusCode.Created);

        // Finalize with the canary email in the body.
        await PostAuthedAsync(
            client, $"/api/v1/sessions/{created.SessionId}/finalize", created.ParticipantToken,
            new { email = CanaryEmail });

        var everything = string.Join("\n", logs);
        everything.Should().NotContain(CanaryName, "display names must never be logged");
        everything.Should().NotContain(CanaryEmail, "email addresses must never be logged");
    }

    private static async Task<CreatedSession> CreateAsync(HttpClient client)
    {
        using var form = new MultipartFormDataContent();
        var image = new ByteArrayContent(TinyJpeg);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(image, "image", "receipt.jpg");
        var response = await client.PostAsync("/api/v1/sessions", form);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatedSession>())!;
    }

    private static async Task WaitForReviewAsync(HttpClient client, string sessionId)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var snapshot = await client.GetFromJsonAsync<SnapshotView>($"/api/v1/sessions/{sessionId}");
            if (snapshot?.State == "Review")
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"session {sessionId} did not reach Review");
    }

    private static async Task PostAuthedAsync(HttpClient client, string path, string token, object? body = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private sealed class CapturingLoggerProvider(ConcurrentQueue<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(sink);

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
                Func<TState, Exception?, string> formatter)
            {
                sink.Enqueue(formatter(state, exception));
                if (exception is not null)
                {
                    sink.Enqueue(exception.ToString());
                }
            }
        }
    }
}
