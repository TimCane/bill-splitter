using BillSplitter.Domain.Abstractions;
using BillSplitter.Domain.Receipts;
using BillSplitter.Domain.Sessions;

namespace BillSplitter.Api.Ocr;

/// <summary>
/// Lazy recovery for a session stuck in <c>Processing</c>: the OCR channel is
/// in-process, so a backend restart loses queued and in-flight jobs. Rather than
/// a watchdog, any read of a session (snapshot GET or hub connect) still
/// <c>Processing</c> more than five minutes after <c>createdAt</c> fails the OCR
/// so the client heals on its next refresh or reconnect - there is no dead end
/// (docs/06-ocr-service.md#backend-job-flow). Because the fail is CAS-conditional
/// on state, it cannot clobber a slow-but-live worker.
/// </summary>
public sealed class StaleOcrRecovery(ISessionStore store, ISessionNotifier notifier, TimeProvider clock)
{
    public static readonly TimeSpan Deadline = TimeSpan.FromMinutes(5);

    private const string Reason = "OCR did not finish";

    /// <summary>Return <paramref name="record"/> unchanged, or - if it is a stale
    /// <c>Processing</c> session - fail its OCR and return the recovered record.</summary>
    public async Task<SessionRecord> RecoverIfStaleAsync(SessionRecord record, CancellationToken ct)
    {
        if (record.Session.State != SessionState.Processing
            || clock.GetUtcNow() - record.Session.CreatedAt <= Deadline)
        {
            return record;
        }

        var recovered = await store.MutateAsync(record.Session.Id, s => s.FailOcr(Reason), ct);
        if (recovered.Session.Ocr.Status == OcrStatus.Failed)
        {
            await notifier.OcrStatusChangedAsync(
                record.Session.Id, OcrStatus.Failed, recovered.Session.Ocr.FailureReason, ct);
            await notifier.SnapshotUpdatedAsync(record.Session.Id, ct);
        }

        return recovered;
    }
}
