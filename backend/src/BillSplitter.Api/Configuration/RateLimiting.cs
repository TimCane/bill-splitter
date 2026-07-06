using System.Globalization;
using System.Threading.RateLimiting;
using BillSplitter.Api.Http;
using BillSplitter.Domain;
using Microsoft.AspNetCore.RateLimiting;

namespace BillSplitter.Api.Configuration;

/// <summary>
/// Per-client-IP rate limiting (docs/10-security-privacy.md#rate-limits). One
/// chained global limiter is the whole policy table: every request draws from
/// the global bucket plus whichever endpoint bucket its method and path match;
/// unmatched buckets are a no-op, so a request only ever pays for the policies
/// that apply to it. Rejections carry <c>Retry-After</c> and the standard
/// <c>rate-limited</c> problem body.
///
/// The partition key is the client IP. Behind the reverse proxy that IP comes
/// from <c>X-Forwarded-For</c> via UseForwardedHeaders trusting only the proxy
/// (docs/13-deployment.md); direct hits key on the socket address.
/// </summary>
public static class RateLimiting
{
    public static void Configure(RateLimiterOptions options, RateLimitOptions limits)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = async (context, _) =>
        {
            // Fixed-window limiters report the time until the window rolls over;
            // surface it so a well-behaved client backs off exactly that long.
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                context.HttpContext.Response.Headers.RetryAfter =
                    ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            }

            await ApiProblem.WriteAsync(context.HttpContext, ErrorCodes.RateLimited);
        };

        options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
            Bucket("global", IsRest, limits.GlobalPerMinute, TimeSpan.FromMinutes(1)),
            Bucket("create", IsCreateSession, limits.CreateSessionPerHour, TimeSpan.FromHours(1)),
            Bucket("join", IsJoin, limits.JoinPerMinute, TimeSpan.FromMinutes(1)),
            Bucket("resolve-min", IsResolveCode, limits.ResolveCodePerMinute, TimeSpan.FromMinutes(1)),
            Bucket("resolve-day", IsResolveCode, limits.ResolveCodePerDay, TimeSpan.FromDays(1)));
    }

    private static PartitionedRateLimiter<HttpContext> Bucket(
        string name, Func<HttpContext, bool> applies, int permitLimit, TimeSpan window) =>
        PartitionedRateLimiter.Create<HttpContext, string>(context => applies(context)
            ? RateLimitPartition.GetFixedWindowLimiter(
                $"{name}:{ClientKey(context)}",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = window })
            : RateLimitPartition.GetNoLimiter("exempt"));

    private static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    // The global bucket counts REST only; the hub carries its own per-connection
    // gesture throttle and /healthz must never be throttled (uptime probe).
    private static bool IsRest(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/api");

    private static bool IsCreateSession(HttpContext context) =>
        HttpMethods.IsPost(context.Request.Method) && MatchSegments(context, "api", "v1", "sessions");

    private static bool IsJoin(HttpContext context) =>
        HttpMethods.IsPost(context.Request.Method)
        && MatchSegments(context, "api", "v1", "sessions", null, "participants");

    private static bool IsResolveCode(HttpContext context) =>
        HttpMethods.IsGet(context.Request.Method) && MatchSegments(context, "api", "v1", "codes", null);

    // Match the path segments case-insensitively to mirror ASP.NET routing - an
    // ordinal match would let a request vary a segment's case (e.g. /api/v1/Codes)
    // to reach the endpoint yet slip past its per-IP limit. A null slot is a
    // wildcard (the session id or code).
    private static bool MatchSegments(HttpContext context, params string?[] expected)
    {
        var segments = Segments(context);
        if (segments.Length != expected.Length)
        {
            return false;
        }

        for (var i = 0; i < expected.Length; i++)
        {
            if (expected[i] is { } want && !segments[i].Equals(want, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string[] Segments(HttpContext context) =>
        context.Request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
}
