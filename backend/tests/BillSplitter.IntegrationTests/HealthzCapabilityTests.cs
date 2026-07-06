using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace BillSplitter.IntegrationTests;

/// <summary>The /healthz email flag is a capability, not a probe: true only when
/// SMTP is configured, and it never changes the status code
/// (docs/04-api-contract.md#get-healthz, M6 A3).</summary>
[Collection(SessionApiCollection.Name)]
public sealed class HealthzCapabilityTests(SessionApiFactory factory)
{
    [Fact]
    public async Task Email_flag_is_false_when_smtp_is_unconfigured()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = (await response.Content.ReadFromJsonAsync<HealthzView>())!;
        report.Email.Should().BeFalse();
    }

    [Fact]
    public async Task Email_flag_is_true_when_smtp_is_configured_and_status_is_unchanged()
    {
        using var configured = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Smtp:Host"] = "smtp.example.com",
                })));
        using var client = configured.CreateClient();

        var response = await client.GetAsync("/healthz");

        // Capability only: the relay is never probed, so a healthy stack still 200s.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = (await response.Content.ReadFromJsonAsync<HealthzView>())!;
        report.Email.Should().BeTrue();
    }
}

public sealed record HealthzView(bool Redis, bool Minio, bool Ocr, bool Email);
