using BillSplitter.Domain.Receipts;
using BillSplitter.Domain.Sessions;

namespace BillSplitter.Domain.Parsing.Validators;

/// <summary>Soft reconciliation check: does <c>sum(items) + tax + tip + service</c>
/// match the printed grand total (docs/15-receipt-parsing.md#validation)? A
/// mismatch beyond tolerance means something was dropped or misread - a parked
/// discount, a price stranded in a drifted column, a misclassified extra - so the
/// host gets a warning to double-check the amounts at review. It is a soft signal,
/// never a hard gate: claims still reconcile exactly downstream in
/// <see cref="SplitCalculator"/> (ADR-0005).</summary>
internal static class TotalsValidator
{
    /// <summary>The same penny-level slack as the copy-collapse reconciliation
    /// (<c>CopyDeduplicator.ToleranceMinor</c>): it only absorbs a
    /// percentage-rounded extra, so a genuine mismatch still trips.</summary>
    private const long ToleranceMinor = 2;

    /// <summary>Returns a host-facing warning when the parse does not reconcile,
    /// or <c>null</c> when it does. A receipt with no printed total
    /// (<c>TotalMinor == 0</c>) has nothing to check against, so it never warns.</summary>
    public static string? Check(IReadOnlyList<ParsedItem> items, Bill bill)
    {
        if (bill.TotalMinor == 0)
        {
            return null;
        }

        var itemsMinor = items.Sum(item => item.PriceMinor);
        var extras = bill.TaxMinor + bill.TipMinor + bill.ServiceMinor;
        if (Math.Abs(itemsMinor + extras - bill.TotalMinor) <= ToleranceMinor)
        {
            return null;
        }

        return "parsed items and extras do not reconcile with the printed total";
    }
}
