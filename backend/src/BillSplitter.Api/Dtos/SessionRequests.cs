namespace BillSplitter.Api.Dtos;

/// <summary>Returned once at create; the raw token is never stored
/// (docs/04-api-contract.md#post-apiv1sessions).</summary>
public sealed record CreateSessionResponse(
    string SessionId,
    string ParticipantId,
    string ParticipantToken,
    string DisplayName);

public sealed record JoinRequest(string DisplayName);

/// <summary>Returned once at join; carries the snapshot so the joiner renders
/// immediately (docs/04-api-contract.md#post-apiv1sessionssessionidparticipants).</summary>
public sealed record JoinResponse(
    string ParticipantId,
    string ParticipantToken,
    SessionSnapshotDto Snapshot);

public sealed record RenameRequest(string DisplayName);

/// <summary>Body for adding or replacing a line item; the id is route/server-owned
/// (docs/04-api-contract.md#item-crud---host-only-state-review-only).</summary>
public sealed record ItemRequest(string Name, int Quantity, long PriceMinor);

/// <summary>Body for the extras + printed total edit
/// (docs/04-api-contract.md#put-apiv1sessionssessionidbill).</summary>
public sealed record BillRequest(
    long TaxMinor,
    long TipMinor,
    long ServiceMinor,
    long TotalMinor,
    string Currency);
