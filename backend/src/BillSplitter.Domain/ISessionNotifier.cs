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
}
