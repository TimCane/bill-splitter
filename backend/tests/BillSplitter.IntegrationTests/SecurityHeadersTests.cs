using FluentAssertions;

namespace BillSplitter.IntegrationTests;

/// <summary>
/// Every response carries the standard security header set
/// (docs/10-security-privacy.md#transport-and-headers, M7 A4).
/// </summary>
[Collection(SessionApiCollection.Name)]
public sealed class SecurityHeadersTests(SessionApiFactory factory)
{
    [Fact]
    public async Task Responses_carry_the_security_headers()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle().Which.Should().Be("nosniff");
        response.Headers.GetValues("Referrer-Policy").Should().ContainSingle().Which.Should().Be("no-referrer");
        var csp = response.Headers.GetValues("Content-Security-Policy").Should().ContainSingle().Subject;
        csp.Should().Contain("default-src 'self'");
        csp.Should().Contain("connect-src 'self' wss:");
        csp.Should().Contain("img-src 'self' data:");
    }
}
