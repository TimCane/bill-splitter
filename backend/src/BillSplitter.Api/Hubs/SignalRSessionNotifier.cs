using BillSplitter.Api.Dtos;
using BillSplitter.Domain.Abstractions;
using BillSplitter.Domain.Receipts;
using BillSplitter.Domain.Sessions;
using Microsoft.AspNetCore.SignalR;

namespace BillSplitter.Api.Hubs;

/// <summary>SignalR implementation of <see cref="ISessionNotifier"/>. Snapshot
/// broadcasts go through the per-session coalescer, which re-reads the session at
/// fire time so REST callers and hub listeners always converge on the same
/// snapshot (docs/05-realtime-contract.md#ordering-and-idempotency).</summary>
public sealed class SignalRSessionNotifier(
    IHubContext<SessionHub> hub,
    SnapshotBroadcastCoalescer coalescer,
    ISessionStore store,
    SnapshotMapper mapper)
    : ISessionNotifier
{
    public const string SnapshotUpdatedEvent = "SnapshotUpdated";
    public const string SessionFinalizedEvent = "SessionFinalized";

    public static string GroupName(string sessionId) => $"session:{sessionId}";

    public Task SnapshotUpdatedAsync(string sessionId, CancellationToken ct)
    {
        coalescer.Schedule(sessionId);
        return Task.CompletedTask;
    }

    // Terminal event: read the finalized session once and send it directly, not
    // through the coalescer - finalize is a single commit, so there is no burst to
    // collapse and the summary must land immediately.
    public async Task SessionFinalizedAsync(string sessionId, CancellationToken ct)
    {
        var record = await store.GetAsync(sessionId, ct);
        if (record is null)
        {
            return;
        }

        var snapshot = mapper.Map(record.Session, record.Ttl);
        await hub.Clients.Group(GroupName(sessionId)).SendAsync(SessionFinalizedEvent, snapshot, ct);
    }

    public Task OcrStatusChangedAsync(
        string sessionId, OcrStatus status, string? failureReason, CancellationToken ct) =>
        hub.Clients.Group(GroupName(sessionId)).SendAsync(
            "OcrStatusChanged",
            new OcrStatusChangedDto(status.ToString(), failureReason),
            ct);

    private sealed record OcrStatusChangedDto(string Status, string? FailureReason);
}
