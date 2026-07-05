namespace BillSplitter.Domain;

/// <summary>One participant's computed share of the bill (docs/04-api-contract.md).</summary>
public sealed record ParticipantTotals(
    long ItemsMinor,
    long TaxMinor,
    long TipMinor,
    long ServiceMinor,
    long UnclaimedMinor,
    long TotalMinor);

/// <summary>Everything the snapshot needs that is derived from split math, keyed
/// for direct lookup. Recomputed from scratch on every snapshot; nothing is
/// cached (docs/02-domain-model.md#split-math-the-core-algorithm).</summary>
public sealed record SplitResult(
    IReadOnlyDictionary<string, ParticipantTotals> Totals,
    IReadOnlyDictionary<(string ItemId, string ParticipantId), long> Allocations,
    long UnclaimedTotalMinor);
