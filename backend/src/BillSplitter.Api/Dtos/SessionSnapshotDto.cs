using BillSplitter.Domain.Sessions;

namespace BillSplitter.Api.Dtos;

/// <summary>
/// The one read model, returned by every snapshot-returning endpoint and carried
/// by the hub's SnapshotUpdated / SessionFinalized events
/// (docs/04-api-contract.md#sessionsnapshotdto). Server-computed fields are derived
/// in <see cref="SnapshotMapper"/>; token hashes never appear here.
/// </summary>
public sealed record SessionSnapshotDto(
    string SessionId,
    int Version,
    string State,
    string Currency,
    DateTimeOffset ExpiresAt,
    string? ShortCode,
    string? JoinUrl,
    string HostParticipantId,
    OcrDto Ocr,
    IReadOnlyList<ParticipantDto> Participants,
    IReadOnlyList<ItemDto> Items,
    BillDto Bill,
    long UnclaimedTotalMinor,
    IReadOnlyList<ParticipantTotalDto> Totals);

public sealed record OcrDto(string Status, string? FailureReason, IReadOnlyList<string> Warnings);

public sealed record ParticipantDto(string ParticipantId, string DisplayName, bool IsHost);

public sealed record ClaimDto(string ParticipantId, int Shares, long AllocatedMinor);

public sealed record ItemDto(
    string ItemId,
    string Name,
    int Quantity,
    long PriceMinor,
    IReadOnlyList<ClaimDto> Claims);

public sealed record BillDto(
    long SubtotalMinor,
    long TaxMinor,
    long TipMinor,
    long ServiceMinor,
    long TotalMinor,
    long ChecksumMinor);

public sealed record ParticipantTotalDto(
    string ParticipantId,
    long ItemsMinor,
    long TaxMinor,
    long TipMinor,
    long ServiceMinor,
    long UnclaimedMinor,
    long TotalMinor);
