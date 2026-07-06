using BillSplitter.Api.Dtos;
using BillSplitter.Domain;
using Microsoft.AspNetCore.SignalR;

namespace BillSplitter.Api.Hubs;

/// <summary>SignalR implementation of <see cref="ISessionNotifier"/>. Snapshot
/// broadcasts go through the per-session coalescer, which re-reads the session at
/// fire time so REST callers and hub listeners always converge on the same
/// snapshot (docs/05-realtime-contract.md#ordering-and-idempotency).</summary>
public sealed class SignalRSessionNotifier(
    IHubContext<SessionHub> hub,
    SnapshotBroadcastCoalescer coalescer)
    : ISessionNotifier
{
    public static string GroupName(string sessionId) => $"session:{sessionId}";

    public Task SnapshotUpdatedAsync(string sessionId, CancellationToken ct)
    {
        coalescer.Schedule(sessionId);
        return Task.CompletedTask;
    }

    public Task OcrStatusChangedAsync(
        string sessionId, OcrStatus status, string? failureReason, CancellationToken ct) =>
        hub.Clients.Group(GroupName(sessionId)).SendAsync(
            "OcrStatusChanged",
            new OcrStatusChangedDto(status.ToString(), failureReason),
            ct);

    private sealed record OcrStatusChangedDto(string Status, string? FailureReason);
}
