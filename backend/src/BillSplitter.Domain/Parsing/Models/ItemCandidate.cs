namespace BillSplitter.Domain.Parsing.Models;

/// <summary>One rule's reading of an item row: the <see cref="ParsedItem"/> it
/// would produce (or a reject, when the row yields no usable item) and the
/// confidence the engine ranks it by (docs/15-receipt-parsing.md#scoring). The
/// highest-confidence candidate wins; a reject that wins parks <see
/// cref="Warning"/> instead of adding an item.</summary>
internal sealed record ItemCandidate(int Confidence, ParsedItem? Item, string? Warning)
{
    public static ItemCandidate ForItem(int confidence, ParsedItem item) =>
        new(confidence, item, null);

    public static ItemCandidate Reject(int confidence, string warning) =>
        new(confidence, null, warning);
}
