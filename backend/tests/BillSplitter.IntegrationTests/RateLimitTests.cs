using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using FluentAssertions;

namespace BillSplitter.IntegrationTests;

/// <summary>
/// Per-client-IP rate limits (docs/10-security-privacy.md#rate-limits, M7 A2). The
/// shared factory loosens every limit; this suite re-hosts with a tight
/// resolve-code window and drives a burst past it to prove the 429 + Retry-After
/// contract without waiting out a real window.
/// </summary>
[Collection(SessionApiCollection.Name)]
public sealed class RateLimitTests(SessionApiFactory factory)
{
    [Fact]
    public async Task A_burst_past_the_resolve_code_limit_gets_429_with_retry_after()
    {
        // Two permits per minute; the third resolve in the same window is rejected.
        using var tight = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["RateLimit:ResolveCodePerMinute"] = "2" })));
        using var client = tight.CreateClient();

        var first = await client.GetAsync("/api/v1/codes/ABCDEF");
        var second = await client.GetAsync("/api/v1/codes/ABCDEF");
        var third = await client.GetAsync("/api/v1/codes/ABCDEF");

        // The endpoint 404s on the unknown code, but the permit is spent before it
        // runs - the limit is on the request, not the outcome.
        first.StatusCode.Should().Be(HttpStatusCode.NotFound);
        second.StatusCode.Should().Be(HttpStatusCode.NotFound);

        third.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        third.Headers.RetryAfter.Should().NotBeNull("a 429 must tell the client how long to back off");
        var problem = await third.Content.ReadFromJsonAsync<ProblemView>();
        problem!.Type.Should().Be("rate-limited");
    }
}
