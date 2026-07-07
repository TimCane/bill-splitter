using BillSplitter.Domain.Receipts;

namespace BillSplitter.Domain.Parsing.Models;

/// <summary>One rule's reading of an item row: the <see cref="ParsedItem"/> it
/// would produce (or a reject, when the row yields no usable item), the
/// confidence the engine ranks it by (docs/15-receipt-parsing.md#scoring) and the
/// <see cref="Evidence"/> behind the reading, surfaced in the parse-decision trace
/// (docs/15-receipt-parsing.md#diagnostics). The highest-confidence candidate
/// wins; a reject that wins parks <see cref="Warning"/> instead of adding an
/// item.</summary>
internal sealed record ItemCandidate(
    int Confidence, ParsedItem? Item, string? Warning, IReadOnlyList<string> Evidence)
{
    public static ItemCandidate ForItem(int confidence, ParsedItem item, params string[] evidence) =>
        new(confidence, item, null, evidence);

    public static ItemCandidate Reject(int confidence, string warning, params string[] evidence) =>
        new(confidence, null, warning, evidence);
}
