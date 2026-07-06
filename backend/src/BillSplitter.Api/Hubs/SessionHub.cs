using BillSplitter.Api.Dtos;
using BillSplitter.Api.Ocr;
using BillSplitter.Domain;
using Microsoft.AspNetCore.SignalR;

namespace BillSplitter.Api.Hubs;

/// <summary>
/// Realtime endpoint for the session. In M3 it is connect-only: it authenticates
/// the query-string token, joins the session group and pushes an immediate
/// snapshot (docs/05-realtime-contract.md#connecting). The claim gesture methods
/// land in M5; downstream events (SnapshotUpdated / OcrStatusChanged) already flow
/// through <see cref="SignalRSessionNotifier"/>.
/// </summary>
public sealed class SessionHub(ISessionStore store, StaleOcrRecovery recovery, SnapshotMapper mapper) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext();
        var sessionId = http?.Request.Query["sessionId"].ToString();
        var token = http?.Request.Query["access_token"].ToString();
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

        await Groups.AddToGroupAsync(Context.ConnectionId, SignalRSessionNotifier.GroupName(sessionId));

        // Immediate snapshot closes any gap between REST rehydrate and connect.
        var snapshot = mapper.Map(record.Session, record.Ttl);
        await Clients.Caller.SendAsync("SnapshotUpdated", snapshot, Context.ConnectionAborted);

        await base.OnConnectedAsync();
    }
}
