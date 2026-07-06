using System.ComponentModel.DataAnnotations;

namespace BillSplitter.Api.Configuration;

/// <summary>
/// Per-client-IP rate limits (docs/10-security-privacy.md#rate-limits). Defaults
/// are the production policy table; integration tests bind tighter values to
/// exercise the 429 path without waiting out a real window.
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    /// <summary><c>POST /sessions</c>.</summary>
    [Range(1, 1_000_000)]
    public int CreateSessionPerHour { get; set; } = 5;

    /// <summary><c>POST .../participants</c>.</summary>
    [Range(1, 1_000_000)]
    public int JoinPerMinute { get; set; } = 20;

    /// <summary><c>GET /codes/{code}</c>, short window.</summary>
    [Range(1, 1_000_000)]
    public int ResolveCodePerMinute { get; set; } = 10;

    /// <summary><c>GET /codes/{code}</c>, daily brute-force ceiling.</summary>
    [Range(1, 1_000_000)]
    public int ResolveCodePerDay { get; set; } = 100;

    /// <summary>All REST endpoints under <c>/api</c>.</summary>
    [Range(1, 10_000_000)]
    public int GlobalPerMinute { get; set; } = 100;
}
