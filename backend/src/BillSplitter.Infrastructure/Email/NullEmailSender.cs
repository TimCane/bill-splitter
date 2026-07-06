using BillSplitter.Domain;

namespace BillSplitter.Infrastructure.Email;

/// <summary>Registered when SMTP is not configured. Email is a capability, not a
/// requirement: with no relay the finalize dialog hides its address field, so this
/// only ever backstops a stray request (docs/04-api-contract.md#get-healthz).</summary>
public sealed class NullEmailSender : IEmailSender
{
    public Task SendSummaryAsync(string toAddress, SummaryEmail summary, CancellationToken ct) =>
        Task.CompletedTask;
}
