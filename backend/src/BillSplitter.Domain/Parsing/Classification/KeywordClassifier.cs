using System.Text.RegularExpressions;
using BillSplitter.Domain.Parsing.Models;

namespace BillSplitter.Domain.Parsing.Classification;

/// <summary>The default <see cref="ILineClassifier"/>: keyword and positional
/// matching on a candidate's name (docs/15-receipt-parsing.md#classify-dont-itemise).
/// Highest-priority match wins, so a "Total Taxes" row reads as tax before total
/// and a VAT breakdown is dropped before it is counted as payable tax. The
/// fixture corpus is the spec (docs/11-testing-strategy.md#receiptparser).</summary>
internal sealed partial class KeywordClassifier : ILineClassifier
{
    // A grouped-receipt category subtotal, a rollup of items listed above it:
    // a category word with a leading count ("8 DRINK", "3 FOOD") or the bare word
    // "FOOD" (never a real item name). A bare "Drinks" is left as an item - some
    // receipts aggregate the drinks onto one priced line.
    [GeneratedRegex(@"^(?:\d+\s+(?:FOOD|DRINKS?|BEVERAGES?)|FOOD)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex CategoryRollup();

    // An item-count summary line ("6 Item(s)"). A subtotal in disguise - drop it.
    [GeneratedRegex(@"^\d+\s+ITEM", RegexOptions.IgnoreCase)]
    private static partial Regex ItemCount();

    // A service-charge label that printed above its amount ("Service charge 12.5%
    // on", "Optional Service charge"). Anchored to the line start so a header
    // that merely mentions a tax code ("... GST 3") is not mistaken for one.
    [GeneratedRegex(@"^(?:OPTIONAL\s+|DISCRETIONARY\s+)?SERVICE\b", RegexOptions.IgnoreCase)]
    private static partial Regex ServiceLabel();

    // A charge line whose label is elsewhere: it opens with a percentage
    // ("12.5% on 41.30"). Recovered from the preceding line or parked.
    [GeneratedRegex(@"^\s*\d{1,3}([.,]\d+)?\s*%")]
    private static partial Regex LeadingPercent();

    public LineType Classify(Candidate candidate)
    {
        var u = candidate.Name.ToUpperInvariant();

        // Subtotals, item-count summaries, category rollups and VAT breakdowns
        // are all reports of amounts we recompute ourselves - drop them. Ordered
        // ahead of Tax so a "20% VAT" breakdown is not counted as payable tax.
        if (IsSubtotal(u))
        {
            return LineType.Subtotal;
        }

        if (IsItemCount(u))
        {
            return LineType.ItemCount;
        }

        if (IsCategoryRollup(u))
        {
            return LineType.CategoryRollup;
        }

        if (IsTaxBreakdown(u))
        {
            return LineType.TaxBreakdown;
        }

        // Tax before total so "Total Taxes" reads as tax, not a grand total.
        if (IsTax(u))
        {
            return LineType.Tax;
        }

        if (IsTip(u))
        {
            return LineType.Tip;
        }

        if (IsService(u))
        {
            return LineType.Service;
        }

        if (IsTotal(u))
        {
            return LineType.Total;
        }

        if (IsPaymentNoise(u))
        {
            return LineType.Payment;
        }

        // Positional: a service charge whose label printed on the line above and
        // whose amount landed alone on this one ("Service charge 12.5% on" /
        // "90.23  11.28", or Defune's "Service Charge" / "...Server 22.68").
        if (IsLoneServiceLabel(candidate))
        {
            return LineType.Service;
        }

        if (IsBareCharge(candidate.Name))
        {
            return LineType.BareCharge;
        }

        return LineType.Item;
    }

    private static bool IsSubtotal(string u) => u.Contains("SUBTOTAL") || u.Contains("SUB TOTAL");

    private static bool IsItemCount(string u) => ItemCount().IsMatch(u);

    private static bool IsCategoryRollup(string u) => CategoryRollup().IsMatch(u);

    private static bool IsTax(string u) => u.Contains("TAX") || u.Contains("VAT") || u.Contains("GST") || u.Contains("IVA");

    // A VAT breakdown summary ("20% VAT Net 18.32", "5.63 IVA 10% 61.95"): it
    // pairs a VAT word with a rate. Plain "TAX 6% 6.20" is excluded - a US sales
    // tax line prints its rate inline and is the real, payable tax.
    private static bool IsTaxBreakdown(string u) =>
        u.Contains('%') && (u.Contains("VAT") || u.Contains("IVA") || u.Contains("GST"));

    private static bool IsTip(string u) => u.Contains("TIP") || u.Contains("GRATUIT");

    private static bool IsService(string u) => u.Contains("SERVICE");

    private static bool IsTotal(string u) =>
        u.Contains("TOTAL") || u.Contains("AMOUNT") || u.Contains("AMT")
        || u.Contains("BALANCE") || u.Contains("TO PAY");

    private static bool IsPaymentNoise(string u) =>
        u.Contains("CASH") || u.Contains("CHANGE") || u.Contains("CARD")
        || u.Contains("VISA") || u.Contains("MASTERCARD") || u.Contains("AUTH");

    // The line above an amount is a lone service-charge label when it opens with
    // "Service"/"Optional Service" and carries no amount of its own - the receipt
    // split the label and its value across two lines, so the value is the charge.
    private static bool IsLoneServiceLabel(Candidate candidate) =>
        !candidate.PreviousHasAmount && ServiceLabel().IsMatch(candidate.PreviousText);

    // A row whose name carries no item (a bare amount left of the price, or a
    // stray percentage) - the residue of a split charge line or a columnar
    // layout. Empty names are handled separately as "price with no name".
    private static bool IsBareCharge(string name) =>
        name.Length > 0 && (LeadingPercent().IsMatch(name) || !name.Any(char.IsLetter));
}
