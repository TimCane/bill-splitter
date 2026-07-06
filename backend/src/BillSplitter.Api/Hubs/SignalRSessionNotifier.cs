using BillSplitter.Api.Dtos;
using BillSplitter.Domain;
using Microsoft.AspNetCore.SignalR;

namespace BillSplitter.Api.Hubs;

/// <summary>SignalR implementation of <see cref="ISessionNotifier"/>. Re-reads the
/// session so REST callers and hub listeners always converge on the same snapshot
/// (docs/04-api-contract.md#conventions).</summary>
public sealed class SignalRSessionNotifier(
    IHubContext<SessionHub> hub,
    ISessionStore store,
    SnapshotMapper mapper)
    : ISessionNotifier
{
    public static string GroupName(string sessionId) => $"session:{sessionId}";

    public async Task SnapshotUpdatedAsync(string sessionId, CancellationToken ct)
    {
        var record = await store.GetAsync(sessionId, ct);
        if (record is null)
        {
            return;
        }

        var snapshot = mapper.Map(record.Session, record.Ttl);
        await hub.Clients.Group(GroupName(sessionId)).SendAsync("SnapshotUpdated", snapshot, ct);
    }

    public Task OcrStatusChangedAsync(
        string sessionId, OcrStatus status, string? failureReason, CancellationToken ct) =>
        hub.Clients.Group(GroupName(sessionId)).SendAsync(
            "OcrStatusChanged",
            new OcrStatusChangedDto(status.ToString(), failureReason),
            ct);

    private sealed record OcrStatusChangedDto(string Status, string? FailureReason);
}
