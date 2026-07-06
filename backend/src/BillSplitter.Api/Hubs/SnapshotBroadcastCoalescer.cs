using System.Collections.Concurrent;
using BillSplitter.Api.Dtos;
using BillSplitter.Domain;
using Microsoft.AspNetCore.SignalR;

namespace BillSplitter.Api.Hubs;

/// <summary>
/// Per-session trailing-edge coalescing for the group <c>SnapshotUpdated</c>
/// broadcast: a burst of mutations collapses into one send of the latest
/// snapshot, ~100ms after the first request in the window
/// (docs/05-realtime-contract.md#ordering-and-idempotency). The snapshot is
/// re-read at fire time, so versions stay strictly increasing. Only the group
/// fan-out is coalesced - REST mutation responses carry their own snapshot inline.
/// </summary>
public sealed class SnapshotBroadcastCoalescer(
    IServiceScopeFactory scopes,
    IHubContext<SessionHub> hub,
    ILogger<SnapshotBroadcastCoalescer> logger)
{
    public static readonly TimeSpan Window = TimeSpan.FromMilliseconds(100);

    // A failed send is the last word on a burst - without it every listener
    // renders stale claims until the next mutation. Retry a few windows before
    // conceding to reconnect-snapshot healing.
    private const int MaxConsecutiveFailures = 3;

    private readonly ConcurrentDictionary<string, PendingFlush> _pending = new();

    /// <summary>Request a broadcast for the session. Returns immediately; the
    /// send happens on the trailing edge of the coalescing window.</summary>
    public void Schedule(string sessionId)
    {
        while (true)
        {
            var flush = _pending.GetOrAdd(sessionId, _ => new PendingFlush());
            lock (flush)
            {
                if (flush.Retired)
                {
                    // Lost a race with the flush loop removing this entry; the
                    // orphan would swallow the request, so fetch a fresh one.
                    continue;
                }

                if (flush.Scheduled)
                {
                    flush.Dirty = true;
                    return;
                }

                flush.Scheduled = true;
            }

            _ = FlushLoopAsync(sessionId, flush);
            return;
        }
    }

    private async Task FlushLoopAsync(string sessionId, PendingFlush flush)
    {
        var failures = 0;
        while (true)
        {
            await Task.Delay(Window);

            lock (flush)
            {
                flush.Dirty = false;
            }

            try
            {
                await BroadcastAsync(sessionId);
                failures = 0;
            }
            catch (Exception ex)
            {
                failures++;
                logger.LogWarning(
                    ex, "coalesced snapshot broadcast failed, attempt {Attempt}", failures);
            }

            lock (flush)
            {
                if (flush.Dirty || (failures > 0 && failures < MaxConsecutiveFailures))
                {
                    // Mutations landed while broadcasting, or the send failed
                    // with retries left: run another window.
                    continue;
                }

                flush.Retired = true;
                _pending.TryRemove(new KeyValuePair<string, PendingFlush>(sessionId, flush));
                return;
            }
        }
    }

    private async Task BroadcastAsync(string sessionId)
    {
        // A fresh scope per fire: the caller's request scope is long gone by the
        // time the trailing edge elapses.
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ISessionStore>();
        var mapper = scope.ServiceProvider.GetRequiredService<SnapshotMapper>();

        var record = await store.GetAsync(sessionId, CancellationToken.None);
        if (record is null)
        {
            return;
        }

        var snapshot = mapper.Map(record.Session, record.Ttl);
        await hub.Clients.Group(SignalRSessionNotifier.GroupName(sessionId))
            .SendAsync("SnapshotUpdated", snapshot);
    }

    private sealed class PendingFlush
    {
        public bool Scheduled;
        public bool Dirty;
        public bool Retired;
    }
}
