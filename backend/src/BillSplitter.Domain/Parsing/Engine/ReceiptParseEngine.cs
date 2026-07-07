using System.Text.RegularExpressions;
using BillSplitter.Domain.Parsing.Classification;
using BillSplitter.Domain.Parsing.Detectors;
using BillSplitter.Domain.Parsing.Models;
using BillSplitter.Domain.Parsing.Multiline;
using BillSplitter.Domain.Parsing.Normalization;
using BillSplitter.Domain.Parsing.Rules;

namespace BillSplitter.Domain.Parsing.Engine;

/// <summary>
/// The rule/engine parsing pipeline behind the <see cref="ReceiptParser"/> facade
/// (docs/adr/0006-receipt-parser-pipeline.md). Pure OCR-lines-to-<see
/// cref="ParsedReceipt"/> heuristics; the fixture corpus is its real spec
/// (docs/11-testing-strategy.md#receiptparser). Phase A extracts each concern
/// (normalize, classify, rules, detectors, validate) out of this class one PR at
/// a time; normalization, classification, the item rules and the bill detectors
/// are now their own units. What remains here is the wiring: build the priced
/// candidates, hand them to the bill and item engines, and assemble the result.
/// </summary>
internal static partial class ReceiptParseEngine
{
    /// <summary>Lines the sidecar recognized below this land in <c>Warnings</c>
    /// rather than as items - too unreliable to seed the split from.</summary>
    private const double ConfidenceFloor = 0.5;

    private static readonly ITextNormalizer Normalizer = new BasicNormalizer();
    private static readonly ILineClassifier Classifier = new KeywordClassifier();
    private static readonly BillDetectionEngine BillEngine = new(Classifier);
    private static readonly ItemSelectionEngine ItemEngine = new();

    // A money token at the end of the line: 1-4 whole digits, a '.' or ',', two
    // fraction digits, optionally preceded by a currency symbol or a minus and
    // optionally followed by a single-letter VAT-class code ("4.00 B"). The end
    // anchor is the reject for "11.00%" - other trailing text means it is not a
    // clean amount row.
    [GeneratedRegex(@"(?<neg>-\s*)?(?<sym>[£€$])?\s*(?<whole>\d{1,4})[.,](?<frac>\d{2})(?:\s+[A-Z])?\s*$")]
    private static partial Regex MoneyAtEnd();

    public static ParsedReceipt Parse(OcrResult result) => ParseTraced(result).Receipt;

    /// <summary>The full parse plus its in-memory decision trace. The public facade
    /// (<see cref="ReceiptParser.Parse"/>) keeps only the <see cref="ParsedReceipt"/>;
    /// the trace is a test-only surface, never logged or wired
    /// (docs/15-receipt-parsing.md#diagnostics).</summary>
    public static TracedReceipt ParseTraced(OcrResult result)
    {
        var warnings = new List<string>();
        var currency = GuessCurrency(result.Lines);

        // Two multi-line pre-passes run before candidates are built. First assemble
        // any item name wrapped across lines onto its price line ("Classic" / "BAO"
        // / "6.50" -> one row), then fold amount-less modifier lines ("+ Bacon",
        // "No Onion") into the priced line above them. Order matters: names must be
        // whole priced rows before modifiers attach, or a modifier would splice into
        // a still-nameless price and block the wrapped-name fold.
        var lines = ModifierMerger.Merge(WrappedNameMerger.Merge(result.Lines));

        var candidates = new List<Candidate>();
        var previousText = string.Empty;
        var previousHasAmount = false;
        foreach (var line in lines)
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

        // The bill engine anchors on the grand total, then reads tax/tip/service
        // off the rows above it and hands back the rest as item rows.
        var detection = BillEngine.Detect(candidates);
        warnings.AddRange(detection.Warnings);

        // Each item row goes through the competing item rules; the engine keeps the
        // highest-confidence reading (an item, or a reject that parks a warning) and
        // reports which rule won so the row can be traced.
        var items = new List<ParsedItem>();
        var itemDecisions = new List<ParseDecision>();
        foreach (var row in detection.ItemRows)
        {
            var selection = ItemEngine.Select(row);
            var best = selection.Best;
            var type = best.Item is not null ? LineType.Item : LineType.BareCharge;
            itemDecisions.Add(new ParseDecision(row.Text, type, best.Confidence, selection.Rule, best.Evidence));
            if (best.Item is not null)
            {
                items.Add(best.Item);
            }
            else
            {
                warnings.Add(best.Warning!);
            }
        }

        // The trace is the bill stage's decisions (grand total, extras, dropped
        // noise) followed by the item stage's; together one decision per priced
        // line. In-memory only - the facade returns just the receipt.
        var trace = new List<ParseDecision>(detection.Decisions.Count + itemDecisions.Count);
        trace.AddRange(detection.Decisions);
        trace.AddRange(itemDecisions);

        var receipt = new ParsedReceipt(items, detection.Bill, currency, warnings);
        return new TracedReceipt(receipt, trace);
    }

    private static long ToMinor(Match money) =>
        AmountMath.ToMinorUnits(money.Groups["whole"].Value, money.Groups["frac"].Value);

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
