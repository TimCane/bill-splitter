using BillSplitter.Domain.Receipts;
using BillSplitter.Domain.Sessions;

namespace BillSplitter.Domain.Parsing.Multiline;

/// <summary>Collapses a receipt photographed as several stacked copies
/// (merchant / customer / kitchen) back to one (docs/15-receipt-parsing.md#multi-line).
/// Fires only when the whole item list is K identical contiguous blocks and keeping
/// one block reconciles with the printed grand total, while keeping them all does
/// not - so a genuine repeat order (two of the same dish, or an ordered pair listed
/// twice on a single-total bill) is preserved, since its one block would not
/// reconcile. Blocks must be at least two items long, so a lone repeated dish
/// ("Burger" / "Burger") is never mistaken for a copy.</summary>
internal static class CopyDeduplicator
{
    /// <summary>A copy has to line up with the total to the penny; the small slack
    /// only absorbs a percentage-rounded extra, matching the reconciliation check.</summary>
    private const long ToleranceMinor = 2;

    public static IReadOnlyList<ParsedItem> Dedupe(IReadOnlyList<ParsedItem> items, Bill bill)
    {
        var extras = bill.TaxMinor + bill.TipMinor + bill.ServiceMinor;
        if (bill.TotalMinor == 0)
        {
            return items;
        }

        // Prefer the most copies: try the shortest block first (largest K), so three
        // stacked copies collapse to one, not to a still-doubled pair.
        for (var blockLength = 2; blockLength <= items.Count / 2; blockLength++)
        {
            if (items.Count % blockLength != 0)
            {
                continue;
            }

            var block = items.Take(blockLength).ToList();
            if (!IsRepeatedBlock(items, block) || !Reconciles(block, extras, bill.TotalMinor))
            {
                continue;
            }

            return block;
        }

        return items;
    }

    private static bool IsRepeatedBlock(IReadOnlyList<ParsedItem> items, IReadOnlyList<ParsedItem> block)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] != block[i % block.Count])
            {
                return false;
            }
        }

        return true;
    }

    private static bool Reconciles(IEnumerable<ParsedItem> block, long extras, long total) =>
        Math.Abs(block.Sum(item => item.PriceMinor) + extras - total) <= ToleranceMinor;
}
