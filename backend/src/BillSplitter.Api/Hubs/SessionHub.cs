using BillSplitter.Api.Dtos;
using BillSplitter.Api.Ocr;
using BillSplitter.Domain.Abstractions;
using BillSplitter.Domain.Common;
using BillSplitter.Domain.Sessions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using SessionOptions = BillSplitter.Api.Configuration.SessionOptions;

namespace BillSplitter.Api.Hubs;

/// <summary>
/// Realtime endpoint for the session. Connect authenticates the participant
/// token (query string, or the bearer header the JS client uses on the
/// long-polling fallback), joins the session group and pushes an immediate
/// snapshot (docs/05-realtime-contract.md#connecting). The claim gestures are idempotent
/// upserts/deletes scoped to the connection's participant, allowed only while
/// <c>Open</c>; errors surface as <see cref="HubException"/> whose message is the
/// stable code (docs/05-realtime-contract.md#hub-errors).
/// </summary>
public sealed class SessionHub(
    ISessionStore store,
    ISessionNotifier notifier,
    StaleOcrRecovery recovery,
    SnapshotMapper mapper,
    IOptions<SessionOptions> sessionOptions) : Hub
{
    private const string SessionIdKey = "sessionId";
    private const string ParticipantIdKey = "participantId";
    private const string GestureWindowKey = "gestureWindow";

    public override async Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext();
        var sessionId = http?.Request.Query["sessionId"].ToString();
        var token = TokenFrom(http);
        if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(token))
        {
            throw new HubException(ErrorCodes.Unauthorized);
        }

        var record = await store.GetAsync(sessionId, Context.ConnectionAborted);
        var participant = record?.Session.FindByTokenHash(TokenHasher.Hash(token));
        if (record is null || participant is null)
        {
            // No session or no match: reject rather than leak that the id exists.
            throw new HubException(ErrorCodes.Unauthorized);
        }

        // A hub connect is a read: heal a session stuck in Processing (docs/06).
        record = await recovery.RecoverIfStaleAsync(record, Context.ConnectionAborted);

        // The gesture methods trust these two values; they are set only here,
        // after the token matched a participant.
        Context.Items[SessionIdKey] = sessionId;
        Context.Items[ParticipantIdKey] = participant.Id;
        Context.Items[GestureWindowKey] = new GestureWindow(sessionOptions.Value.HubGesturesPerSecond);

        await Groups.AddToGroupAsync(Context.ConnectionId, SignalRSessionNotifier.GroupName(sessionId));

        // Immediate snapshot closes any gap between REST rehydrate and connect.
        var snapshot = mapper.Map(record.Session, record.Ttl);
        await Clients.Caller.SendAsync(
            SignalRSessionNotifier.SnapshotUpdatedEvent, snapshot, Context.ConnectionAborted);

        await base.OnConnectedAsync();
    }

    // The JS client puts the token in the query for WebSockets/SSE (no headers
    // there) but sends a bearer header on the long-polling fallback.
    private static string? TokenFrom(HttpContext? http)
    {
        var token = http?.Request.Query["access_token"].ToString();
        if (!string.IsNullOrEmpty(token))
        {
            return token;
        }

        const string scheme = "Bearer ";
        var authorization = http?.Request.Headers.Authorization.ToString();
        return authorization?.StartsWith(scheme, StringComparison.OrdinalIgnoreCase) == true
            ? authorization[scheme.Length..]
            : null;
    }

    /// <summary>Upsert my claim with one share - sugar for <c>SetShares(itemId, 1)</c>.</summary>
    public Task ClaimItem(string itemId) =>
        MutateAsync((session, participantId) => session.ClaimItem(itemId, participantId));

    /// <summary>Remove my claim; no-op if I hold none.</summary>
    public Task UnclaimItem(string itemId) =>
        MutateAsync((session, participantId) => session.UnclaimItem(itemId, participantId));

    /// <summary>Upsert my claim at the given weight (1-99).</summary>
    public Task SetShares(string itemId, int shares) =>
        MutateAsync((session, participantId) => session.SetShares(itemId, participantId, shares));

    // One funnel for the three gestures: throttle, mutate under CAS, broadcast.
    // Domain rules throw DomainException before anything is written; SignalR has
    // no middleware, so the stable code is re-thrown here as the HubException.
    private async Task MutateAsync(Action<Session, string> gesture)
    {
        var sessionId = (string)Context.Items[SessionIdKey]!;
        var participantId = (string)Context.Items[ParticipantIdKey]!;
        var window = (GestureWindow)Context.Items[GestureWindowKey]!;

        if (!window.TryAcquire())
        {
            throw new HubException(ErrorCodes.RateLimited);
        }

        try
        {
            await store.MutateAsync(sessionId, s => gesture(s, participantId), Context.ConnectionAborted);
        }
        catch (DomainException ex)
        {
            throw new HubException(ex.Code);
        }

        await notifier.SnapshotUpdatedAsync(sessionId, Context.ConnectionAborted);
    }

    // Hand-rolled fixed window instead of a BCL rate limiter: no per-connection
    // replenishment timer, and nothing to dispose - the limiter variant raced
    // OnDisconnectedAsync's Dispose against in-flight gestures.
    private sealed class GestureWindow(int permitsPerSecond)
    {
        private long _windowStart = Environment.TickCount64;
        private int _count;

        public bool TryAcquire()
        {
            lock (this)
            {
                var now = Environment.TickCount64;
                if (now - _windowStart >= 1000)
                {
                    _windowStart = now;
                    _count = 0;
                }

                return ++_count <= permitsPerSecond;
            }
        }
    }
}
