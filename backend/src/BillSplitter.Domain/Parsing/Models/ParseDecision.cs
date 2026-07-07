namespace BillSplitter.Domain.Parsing.Models;

/// <summary>Why the engine classified a line the way it did: the winning <see
/// cref="Rule"/>, its <see cref="Score"/> and the <see cref="Evidence"/> behind it.
/// <see cref="Score"/> is the item engine's confidence for an item row; keyword
/// bill classification is priority-ordered, not scored, so those decisions carry
/// <c>0</c>. Held in-memory for corpus tests only - no receipt text reaches
/// production logs (docs/10-security-privacy.md,
/// docs/15-receipt-parsing.md#diagnostics).</summary>
internal sealed record ParseDecision(
    string Text,
    LineType Type,
    int Score,
    string Rule,
    IReadOnlyList<string> Evidence);
