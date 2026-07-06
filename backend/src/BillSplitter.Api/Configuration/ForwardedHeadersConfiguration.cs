using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace BillSplitter.Api.Configuration;

/// <summary>
/// Trust <c>X-Forwarded-For</c>/<c>-Proto</c> from the reverse proxy so the app
/// sees the real client IP (the rate limiter keys on it) and scheme (HTTPS).
/// Off by default - a directly exposed app must never honour spoofable forwarded
/// headers; production turns it on and names the proxy
/// (docs/13-deployment.md#topology, docs/10-security-privacy.md#rate-limits).
/// </summary>
public sealed class ForwardedProxyOptions
{
    public const string SectionName = "ForwardedHeaders";

    public bool Enabled { get; set; }

    /// <summary>How many proxy hops to trust. One reverse proxy = 1.</summary>
    [Range(1, 8)]
    public int ForwardLimit { get; set; } = 1;

    /// <summary>Exact proxy addresses to trust (e.g. a fixed proxy IP).</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>CIDR networks to trust - the Docker network the proxy sits on
    /// when its address is not fixed (e.g. <c>10.0.0.0/8</c>).</summary>
    public string[] KnownNetworks { get; set; } = [];
}

public static class ForwardedHeadersConfiguration
{
    /// <summary>Bind and register the forwarded-headers trust. Returns whether it
    /// is enabled so the pipeline knows to call <c>UseForwardedHeaders</c>.</summary>
    public static bool AddProxyForwardedHeaders(this IServiceCollection services, IConfiguration config)
    {
        var options = config.GetSection(ForwardedProxyOptions.SectionName).Get<ForwardedProxyOptions>()
            ?? new ForwardedProxyOptions();
        if (!options.Enabled)
        {
            return false;
        }

        services.Configure<ForwardedHeadersOptions>(forwarded =>
        {
            forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            forwarded.ForwardLimit = options.ForwardLimit;

            // Clear the loopback defaults: only the addresses named here are
            // trusted to set the forwarded headers.
            forwarded.KnownProxies.Clear();
            forwarded.KnownIPNetworks.Clear();
            foreach (var proxy in options.KnownProxies)
            {
                forwarded.KnownProxies.Add(IPAddress.Parse(proxy));
            }

            foreach (var network in options.KnownNetworks)
            {
                forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });

        return true;
    }
}
