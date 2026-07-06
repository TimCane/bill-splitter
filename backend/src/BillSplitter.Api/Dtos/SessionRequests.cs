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
