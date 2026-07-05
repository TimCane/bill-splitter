namespace BillSplitter.Api.Configuration;

/// <summary>
/// SMTP is optional: email is a capability, not a requirement. When
/// <see cref="Host"/> is unset the finalize flow drops its email field and
/// /healthz reports <c>email: false</c>. Wired in M6.
/// </summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string? From { get; set; }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(Host);
}
