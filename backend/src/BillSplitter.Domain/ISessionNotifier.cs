namespace BillSplitter.Domain;

/// <summary>
/// Broadcasts live session updates. Implemented in the Api over SignalR so Domain
/// and Infrastructure never reference the hub (docs/07-backend-design.md). Callers
/// pass only a session id; the implementation builds and sends the snapshot.
/// </summary>
public interface ISessionNotifier
{
    /// <summary>Broadcast the current snapshot to the session's hub group after a
    /// successful mutation (docs/05-realtime-contract.md#server---client-events).</summary>
    Task SnapshotUpdatedAsync(string sessionId, CancellationToken ct);

    /// <summary>Broadcast the terminal <c>SessionFinalized</c> event carrying the
    /// finalized snapshot, so clients switch to the summary and stop accepting input
    /// without diffing state (docs/05-realtime-contract.md#server---client-events).</summary>
    Task SessionFinalizedAsync(string sessionId, CancellationToken ct);

    /// <summary>Broadcast an OCR status transition. A hint only: it is always paired
    /// with a <c>SnapshotUpdated</c> carrying the authoritative state, so a stale
    /// hint is harmless (docs/05-realtime-contract.md#server---client-events).</summary>
    Task OcrStatusChangedAsync(string sessionId, OcrStatus status, string? failureReason, CancellationToken ct);
}
