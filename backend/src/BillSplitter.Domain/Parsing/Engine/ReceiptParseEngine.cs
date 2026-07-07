using System.Globalization;
using System.Text.RegularExpressions;
using BillSplitter.Domain.Parsing.Models;
using BillSplitter.Domain.Parsing.Normalization;

namespace BillSplitter.Domain.Parsing.Engine;

/// <summary>
/// The rule/engine parsing pipeline behind the <see cref="ReceiptParser"/> facade
/// (docs/adr/0006-receipt-parser-pipeline.md). Pure OCR-lines-to-<see
/// cref="ParsedReceipt"/> heuristics; the fixture corpus is its real spec
/// (docs/11-testing-strategy.md#receiptparser). Phase A extracts each concern
/// (normalize, classify, rules, detectors, validate) out of this class one PR at
/// a time; today it still runs the heuristics inline.
/// </summary>
internal static partial class ReceiptParseEngine
{
    /// <summary>Lines the sidecar recognized below this land in <c>Warnings</c>
    /// rather than as items - too unreliable to seed the split from.</summary>
    private const double ConfidenceFloor = 0.5;

    private static readonly ITextNormalizer Normalizer = new BasicNormalizer();

    // A money token at the end of the line: 1-4 whole digits, a '.' or ',', two
    // fraction digits, optionally preceded by a currency symbol or a minus and
    // optionally followed by a single-letter VAT-class code ("4.00 B"). The end
    // anchor is the reject for "11.00%" - other trailing text means it is not a
    // clean amount row.
    [GeneratedRegex(@"(?<neg>-\s*)?(?<sym>[£€$])?\s*(?<whole>\d{1,4})[.,](?<frac>\d{2})(?:\s+[A-Z])?\s*$")]
    private static partial Regex MoneyAtEnd();

    // A bare money token at the end of a name, e.g. the per-unit column in
    // "2 BREAD 2.00 4.00" once the line total is removed.
    [GeneratedRegex(@"(?<sym>[£€$])?\s*(?<uwhole>\d{1,4})[.,](?<ufrac>\d{2})\s*$")]
    private static partial Regex TrailingMoney();

    // A charge line whose label is elsewhere: it opens with a percentage
    // ("12.5% on 41.30"). Recovered from the preceding line or parked.
    [GeneratedRegex(@"^\s*\d{1,3}([.,]\d+)?\s*%")]
    private static partial Regex LeadingPercent();

    // A grouped-receipt category subtotal, a rollup of items listed above it:
    // a category word with a leading count ("8 DRINK", "3 FOOD") or the bare word
    // "FOOD" (never a real item name). A bare "Drinks" is left as an item - some
    // receipts aggregate the drinks onto one priced line.
    [GeneratedRegex(@"^(?:\d+\s+(?:FOOD|DRINKS?|BEVERAGES?)|FOOD)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex CategoryRollup();

    // A service-charge label that printed above its amount ("Service charge 12.5%
    // on", "Optional Service charge"). Anchored to the line start so a header
    // that merely mentions a tax code ("... GST 3") is not mistaken for one.
    [GeneratedRegex(@"^(?:OPTIONAL\s+|DISCRETIONARY\s+)?SERVICE\b", RegexOptions.IgnoreCase)]
    private static partial Regex ServiceLabel();

    // A per-unit detail line printed under its item ("2 @ $35.50"): once its own
    // price is stripped the name is only "2 @", so the whole name is a count and
    // an "@" (with any leftover digits). An item like "2 @ 6.40 Miso Soup" has a
    // real name after and is not matched.
    [GeneratedRegex(@"^\d+\s*@\s*[£€$]?[\d.,]*\s*$")]
    private static partial Regex UnitPriceDetail();

    // A leading quantity: "2 ", "2x ", "3 X " -> the count, stripped from the name.
    [GeneratedRegex(@"^(?<qty>\d{1,2})\s?[xX]?\s+")]
    private static partial Regex LeadingQuantity();

    // A "#12"-style item code, dropped from the name.
    [GeneratedRegex(@"#\S+")]
    private static partial Regex ItemCode();

    // A "@ 6.50" per-unit price annotation printed alongside the line total
    // ("5 CLASSIC BAO @6.50 ... 32.50"). Noise in the name; the row's own amount
    // is already the line total.
    [GeneratedRegex(@"@\s*[£€$]?\d{1,4}[.,]\d{2}")]
    private static partial Regex AtUnitPrice();

    // An item-count summary line ("6 Item(s)"). A subtotal in disguise - drop it.
    [GeneratedRegex(@"^\d+\s+ITEM", RegexOptions.IgnoreCase)]
    private static partial Regex ItemCount();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static ParsedReceipt Parse(OcrResult result)
    {
        var warnings = new List<string>();
        var currency = GuessCurrency(result.Lines);

        var candidates = new List<Candidate>();
        var previousText = string.Empty;
        foreach (var line in result.Lines)
        {
            var text = Normalizer.Normalize(line.Text ?? string.Empty);
            var priorText = previousText;
            previousText = text; // advance for every line, so labels above amounts survive

            var money = MoneyAtEnd().Match(text);
            if (!money.Success)
            {
                // No amount: a header, address or free-text line. Ignored, not a warning.
                continue;
            }

            if (line.Confidence < ConfidenceFloor)
            {
                warnings.Add($"low-confidence line ignored: {text}");
                continue;
            }

            if (money.Groups["neg"].Success)
            {
                // Discounts print negative; MVP parks them for the host, unmodeled.
                warnings.Add($"negative amount ignored: {text}");
                continue;
            }

            var name = text[..money.Index].Trim();
            candidates.Add(new Candidate(line.Box.Y, ToMinor(money), name, text, priorText));
        }

        // Locate the grand total first: it is the lowest TOTAL row on the receipt
        // (grand totals print last); a same-row tie takes the larger amount.
        Candidate? total = null;
        foreach (var candidate in candidates)
        {
            var upper = candidate.Name.ToUpperInvariant();
            if (IsSubtotal(upper) || IsItemCount(upper) || IsTaxBreakdown(upper) || !IsTotal(upper))
            {
                continue;
            }

            if (total is null
                || candidate.Y > total.Y
                || (candidate.Y == total.Y && candidate.Amount > total.Amount))
            {
                total = candidate;
            }
        }

        var totalY = total?.Y ?? int.MaxValue;

        // Classify only what sits above the grand total. Everything at or below it
        // - VAT breakdowns, payment lines, "divide by N" hints - is trailing noise
        // the receipt prints after the amount due, not tax/tip/service or items.
        long tax = 0, tip = 0, service = 0;
        var itemRows = new List<Candidate>();
        foreach (var candidate in candidates)
        {
            if (ReferenceEquals(candidate, total) || candidate.Y >= totalY)
            {
                continue;
            }

            var upper = candidate.Name.ToUpperInvariant();
            if (IsSubtotal(upper) || IsItemCount(upper) || IsCategoryRollup(upper) || IsTaxBreakdown(upper))
            {
                continue; // Subtotals, rollups and VAT breakdowns: we compute our own.
            }

            if (IsTax(upper))
            {
                // Before the total check so "Total Taxes" reads as tax, not a total.
                tax = candidate.Amount;
            }
            else if (IsTip(upper))
            {
                tip = candidate.Amount;
            }
            else if (IsService(upper))
            {
                service = candidate.Amount;
            }
            else if (IsTotal(upper))
            {
                continue; // An intermediate total ("Item Total"): we compute our own.
            }
            else if (IsPaymentNoise(upper))
            {
                // Payment tender lines: ignore.
            }
            else if (LoneServiceLabel(candidate.PreviousText))
            {
                // A service charge whose label printed on the line above and whose
                // amount landed alone on this one ("Service charge 12.5% on" /
                // "90.23  11.28", or Defune's "Service Charge" / "...Server 22.68").
                service = candidate.Amount;
            }
            else if (UnitPriceDetail().IsMatch(candidate.Name))
            {
                // "2 @ $35.50" under its item: the item already has the line total.
            }
            else if (IsBareCharge(candidate.Name))
            {
                // A bare amount with no recoverable label - a columnar misread.
                warnings.Add($"unreadable amount ignored: {candidate.Text}");
            }
            else
            {
                itemRows.Add(candidate);
            }
        }

        var items = new List<ParsedItem>();
        foreach (var row in itemRows)
        {
            var (quantity, name) = ExtractQuantity(CleanName(row.Name));
            name = StripUnitPrice(name, row.Amount, quantity);
            if (name.Length == 0)
            {
                warnings.Add($"price with no name ignored: {row.Text}");
                continue;
            }

            items.Add(new ParsedItem(name, quantity, row.Amount));
        }

        var bill = new Bill(tax, tip, service, total?.Amount ?? 0);
        return new ParsedReceipt(items, bill, currency, warnings);
    }

    private static long ToMinor(Match money) =>
        (long.Parse(money.Groups["whole"].Value, CultureInfo.InvariantCulture) * 100)
        + long.Parse(money.Groups["frac"].Value, CultureInfo.InvariantCulture);

    private static string GuessCurrency(IReadOnlyList<OcrLine> lines)
    {
        foreach (var line in lines)
        {
            foreach (var ch in line.Text ?? string.Empty)
            {
                switch (ch)
                {
                    case '£': return "GBP";
                    case '€': return "EUR";
                    case '$': return "USD";
                }
            }
        }

        return Session.DefaultCurrency;
    }

    private static string CleanName(string raw)
    {
        var name = ItemCode().Replace(raw, string.Empty);
        name = AtUnitPrice().Replace(name, string.Empty); // strip "@ 6.50" unit prices
        name = name.Trim().TrimEnd('.', ' ', '-', '=', '@'); // strip dot leaders and separators
        return Whitespace().Replace(name, " ").Trim();
    }

    private static (int Quantity, string Name) ExtractQuantity(string name)
    {
        var match = LeadingQuantity().Match(name);
        if (!match.Success)
        {
            return (1, name);
        }

        var rest = name[match.Length..].Trim();
        var quantity = int.Parse(match.Groups["qty"].Value, CultureInfo.InvariantCulture);
        return rest.Length > 0 && quantity >= 1 ? (quantity, rest) : (1, name);
    }

    // Drop the per-unit price column ("2 ROAST BEEF 27.00" -> "ROAST BEEF") only
    // when it is unambiguous: a multiple quantity whose trailing number times the
    // count equals the line total. A single item's trailing number is left alone.
    private static string StripUnitPrice(string name, long amount, int quantity)
    {
        if (quantity <= 1)
        {
            return name;
        }

        var match = TrailingMoney().Match(name);
        if (!match.Success)
        {
            return name;
        }

        var unit = (long.Parse(match.Groups["uwhole"].Value, CultureInfo.InvariantCulture) * 100)
            + long.Parse(match.Groups["ufrac"].Value, CultureInfo.InvariantCulture);
        return unit * quantity == amount
            ? name[..match.Index].Trim().TrimEnd('.', ' ', '-')
            : name;
    }

    // A row whose name carries no item (a bare amount left of the price, or a
    // stray percentage) - the residue of a split charge line or a columnar
    // layout. Empty names are handled separately as "price with no name".
    private static bool IsBareCharge(string name) =>
        name.Length > 0 && (LeadingPercent().IsMatch(name) || !name.Any(char.IsLetter));

    // The line above an amount is a lone service-charge label when it opens with
    // "Service"/"Optional Service" and carries no amount of its own - the receipt
    // split the label and its value across two lines, so the value is the charge.
    private static bool LoneServiceLabel(string priorText) =>
        !MoneyAtEnd().Match(priorText).Success && ServiceLabel().IsMatch(priorText);

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
}
