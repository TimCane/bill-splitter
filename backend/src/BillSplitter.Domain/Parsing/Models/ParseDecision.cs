namespace BillSplitter.Domain.Parsing.Models;

/// <summary>Why the engine classified a line the way it did: the winning rule,
/// its score and the evidence behind it. Held in-memory for corpus tests only -
/// no receipt text reaches production logs (docs/10-security-privacy.md,
/// docs/15-receipt-parsing.md#diagnostics).</summary>
internal sealed record ParseDecision(
    string Text,
    LineType Type,
    int Score,
    string Rule,
    IReadOnlyList<string> Evidence);
