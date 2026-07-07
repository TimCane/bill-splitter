namespace BillSplitter.Domain.Abstractions;

/// <summary>
/// Sends the one-off summary email. Implemented over SMTP in Infrastructure so the
/// domain never knows the transport. The address is passed in per send and is
/// never stored or logged (docs/04-api-contract.md#post-apiv1sessionssessionidfinalize,
/// docs/10-security-privacy.md#ephemerality-guarantees).
/// </summary>
public interface IEmailSender
{
    /// <summary>Render and send the finalized summary to <paramref name="toAddress"/>.
    /// Fired in the background after finalize; failures are swallowed and logged as
    /// exception type and SMTP status only, never surfaced to the caller.</summary>
    Task SendSummaryAsync(string toAddress, SummaryEmail summary, CancellationToken ct);
}

/// <summary>The finalized figures the summary email renders. Built from the snapshot
/// at send time; carries no token and is never persisted.</summary>
public sealed record SummaryEmail(
    string Currency,
    long TotalMinor,
    long UnclaimedTotalMinor,
    IReadOnlyList<SummaryEmailLine> Lines);

/// <summary>One per-person line: the display name and their finalized total.</summary>
public sealed record SummaryEmailLine(string DisplayName, long TotalMinor);
