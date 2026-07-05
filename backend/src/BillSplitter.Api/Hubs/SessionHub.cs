using Microsoft.AspNetCore.SignalR;

namespace BillSplitter.Api.Hubs;

/// <summary>
/// Realtime endpoint for claim gestures. Gesture methods
/// (ClaimItem / UnclaimItem / SetShares) and snapshot broadcast land in M5
/// (docs/05-realtime-contract.md); the connection is mapped now so the
/// pipeline is complete.
/// </summary>
public sealed class SessionHub : Hub
{
}
