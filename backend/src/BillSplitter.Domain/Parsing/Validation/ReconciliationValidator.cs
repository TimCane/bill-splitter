using BillSplitter.Domain;

namespace BillSplitter.Domain.Parsing.Validation;

/// <summary>Cross-line check that runs last: the parsed items plus the extras
/// (tax + tip + service) should add up to the printed grand total. A mismatch is
/// a discard-shaped signal for the host - an item the parser missed or invented,
/// or a total the OCR misread - so it lands in <c>Warnings</c> rather than
/// silently seeding an unbalanced split (docs/15-receipt-parsing.md#validation).
/// Pure and stateless; not a per-line classification, so it takes no part in the
/// parse trace.</summary>
internal static class ReconciliationValidator
{
    /// <summary>Absolute slack, in minor units, before a gap is reported. Absorbs
    /// the single-unit rounding that percentage tax/service lines can leave behind
    /// without masking a genuinely missing or phantom item.</summary>
    private const long ToleranceMinor = 2;

    /// <summary>The reconcile warning, or <c>null</c> when the totals balance (or
    /// there is nothing to balance against). Skips silently when no grand total was
    /// anchored (<paramref name="bill"/> total is zero) and when a negative amount
    /// was already parked: an unmodeled discount necessarily unbalances the sum, so
    /// reporting the gap would only restate what the discount warning already says.</summary>
    public static string? Check(
        IReadOnlyList<ParsedItem> items,
        Bill bill,
        IReadOnlyList<string> warnings)
    {
        var total = bill.TotalMinor;
        if (total == 0)
        {
            return null;
        }

        foreach (var warning in warnings)
        {
            if (warning.StartsWith("negative amount ignored", StringComparison.Ordinal))
            {
                return null;
            }
        }

        var calc = bill.TaxMinor + bill.TipMinor + bill.ServiceMinor;
        foreach (var item in items)
        {
            calc += item.PriceMinor;
        }

        return Math.Abs(calc - total) > ToleranceMinor
            ? $"totals do not reconcile: items and extras come to {calc} but the receipt total is {total}"
            : null;
    }
}
