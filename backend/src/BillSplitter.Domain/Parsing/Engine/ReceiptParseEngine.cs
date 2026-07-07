using System.Globalization;
using System.Text.RegularExpressions;
using BillSplitter.Domain.Parsing.Classification;
using BillSplitter.Domain.Parsing.Models;
using BillSplitter.Domain.Parsing.Normalization;
using BillSplitter.Domain.Parsing.Rules;

namespace BillSplitter.Domain.Parsing.Engine;

/// <summary>
/// The rule/engine parsing pipeline behind the <see cref="ReceiptParser"/> facade
/// (docs/adr/0006-receipt-parser-pipeline.md). Pure OCR-lines-to-<see
/// cref="ParsedReceipt"/> heuristics; the fixture corpus is its real spec
/// (docs/11-testing-strategy.md#receiptparser). Phase A extracts each concern
/// (normalize, classify, rules, detectors, validate) out of this class one PR at
/// a time; normalization, classification and the item rules are now their own
/// units, and the engine wires them together while still detecting the bill lines
/// (total, tax, tip, service) inline pending A5.
/// </summary>
internal static partial class ReceiptParseEngine
{
    /// <summary>Lines the sidecar recognized below this land in <c>Warnings</c>
    /// rather than as items - too unreliable to seed the split from.</summary>
    private const double ConfidenceFloor = 0.5;

    private static readonly ITextNormalizer Normalizer = new BasicNormalizer();
    private static readonly ILineClassifier Classifier = new KeywordClassifier();
    private static readonly ItemSelectionEngine ItemEngine = new();

    // A money token at the end of the line: 1-4 whole digits, a '.' or ',', two
    // fraction digits, optionally preceded by a currency symbol or a minus and
    // optionally followed by a single-letter VAT-class code ("4.00 B"). The end
    // anchor is the reject for "11.00%" - other trailing text means it is not a
    // clean amount row.
    [GeneratedRegex(@"(?<neg>-\s*)?(?<sym>[£€$])?\s*(?<whole>\d{1,4})[.,](?<frac>\d{2})(?:\s+[A-Z])?\s*$")]
    private static partial Regex MoneyAtEnd();

    // A per-unit detail line printed under its item ("2 @ $35.50"): once its own
    // price is stripped the name is only "2 @", so the whole name is a count and
    // an "@" (with any leftover digits). An item like "2 @ 6.40 Miso Soup" has a
    // real name after and is not matched.
    [GeneratedRegex(@"^\d+\s*@\s*[£€$]?[\d.,]*\s*$")]
    private static partial Regex UnitPriceDetail();

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

        // Each item row goes through the competing item rules; the engine keeps the
        // highest-confidence reading (an item, or a reject that parks a warning).
        var items = new List<ParsedItem>();
        foreach (var row in itemRows)
        {
            var selected = ItemEngine.Select(row);
            if (selected.Item is not null)
            {
                items.Add(selected.Item);
            }
            else
            {
                warnings.Add(selected.Warning!);
            }
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
}
