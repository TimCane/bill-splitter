using System.Globalization;
using System.Text.RegularExpressions;
using BillSplitter.Domain.Parsing.Classification;
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
    private static readonly ILineClassifier Classifier = new KeywordClassifier();

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

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static ParsedReceipt Parse(OcrResult result)
    {
        var warnings = new List<string>();
        var currency = GuessCurrency(result.Lines);

        var candidates = new List<Candidate>();
        var previousText = string.Empty;
        var previousHasAmount = false;
        foreach (var line in result.Lines)
        {
            var text = Normalizer.Normalize(line.Text ?? string.Empty);
            var priorText = previousText;
            var priorHasAmount = previousHasAmount;

            var money = MoneyAtEnd().Match(text);
            // Advance for every line, so a label printed above its amount survives
            // to the next candidate as PreviousText.
            previousText = text;
            previousHasAmount = money.Success;
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
            candidates.Add(new Candidate(line.Box.Y, ToMinor(money), name, text, priorText, priorHasAmount));
        }

        // Locate the grand total first: it is the lowest TOTAL row on the receipt
        // (grand totals print last); a same-row tie takes the larger amount.
        Candidate? total = null;
        foreach (var candidate in candidates)
        {
            if (!Classifier.IsGrandTotalCandidate(candidate))
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

            var type = Classifier.Classify(candidate);
            if (type is LineType.Subtotal or LineType.ItemCount
                or LineType.CategoryRollup or LineType.TaxBreakdown)
            {
                continue; // Subtotals, rollups and VAT breakdowns: we compute our own.
            }

            if (type == LineType.Tax)
            {
                tax = candidate.Amount;
            }
            else if (type == LineType.Tip)
            {
                tip = candidate.Amount;
            }
            else if (type == LineType.Service)
            {
                service = candidate.Amount;
            }
            else if (type == LineType.Total)
            {
                continue; // An intermediate total ("Item Total"): we compute our own.
            }
            else if (type == LineType.Payment)
            {
                // Payment tender lines: ignore.
            }
            else if (UnitPriceDetail().IsMatch(candidate.Name))
            {
                // "2 @ $35.50" under its item: the item already has the line total.
            }
            else if (type == LineType.BareCharge)
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
}
