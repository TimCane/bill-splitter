using System.Linq;
using System.Text.RegularExpressions;
using BillSplitter.Domain.Parsing.Classification;
using BillSplitter.Domain.Parsing.Detectors;
using BillSplitter.Domain.Parsing.Models;
using BillSplitter.Domain.Parsing.Multiline;
using BillSplitter.Domain.Parsing.Normalization;
using BillSplitter.Domain.Parsing.Rules;
using BillSplitter.Domain.Parsing.Spatial;
using BillSplitter.Domain.Receipts;
using BillSplitter.Domain.Sessions;

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

    // A money token (ReceiptPatterns.Money) at the end of the line. The end anchor
    // is the reject for "11.00%" - other trailing text means it is not a clean
    // amount row.
    [GeneratedRegex(ReceiptPatterns.Money + @"\s*$")]
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

        // Repair money-span misreads first, on every line, so the pre-passes below
        // see corrected prices. They gate folding on a real money token ("E6.5O" is
        // not one until repaired), so a misread price on a wrapped-name row or above
        // a modifier line would otherwise never fold. GuessCurrency stays on the raw
        // lines: a repaired "E" -> "£" must not preempt a genuine currency symbol.
        var repaired = result.Lines
            .Select(line => line with { Text = MoneyMisreadRepair.Repair(line.Text ?? string.Empty) })
            .ToList();

        // Two multi-line pre-passes run before candidates are built. First assemble
        // any item name wrapped across lines onto its price line ("Classic" / "BAO"
        // / "6.50" -> one row), then fold amount-less modifier lines ("+ Bacon",
        // "No Onion") into the priced line above them. Order matters: names must be
        // whole priced rows before modifiers attach, or a modifier would splice into
        // a still-nameless price and block the wrapped-name fold.
        var lines = ModifierMerger.Merge(WrappedNameMerger.Merge(repaired));

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
            candidates.Add(new Candidate(line.Box.Y, line.Box.X, ToMinor(money), name, text, priorText, priorHasAmount));
        }

        // OCR can emit priced rows out of reading order; reorder them by bounding box
        // so the item list reads top-to-bottom. Priced candidates only, and late on
        // purpose: the grand total is anchored positionally (max Box.Y) either way, and
        // the multi-line pre-passes and the PreviousText capture above already ran in
        // scan order - so a modifier fold or a label-printed-above-its-amount on a
        // genuinely scrambled receipt is paired upstream, not repaired here. Sorting
        // whole priced rows only changes their order, never their pairing, so a
        // columnar layout (name and price in separate columns) still parks. Gated: an
        // already-ordered receipt is returned untouched.
        var ordered = BoxOrderer.Order(candidates);

        // The bill engine anchors on the grand total, then reads tax/tip/service
        // off the rows above it and hands back the rest as item rows.
        var detection = BillEngine.Detect(ordered);
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

        // A receipt shot as stacked merchant/customer/kitchen copies parses its
        // items more than once; collapse them back to one when a single copy
        // reconciles with the printed total. A genuine repeat order is preserved.
        var deduped = CopyDeduplicator.Dedupe(items, detection.Bill);

        // A collapsed copy also raised each of its warnings once per copy (a
        // low-confidence or negative line lives in every stacked copy but never in
        // the item list, so the collapse cannot reach it); fold those to distinct so
        // the host sees each warning once, matching the single item list.
        var reportedWarnings = deduped.Count < items.Count ? warnings.Distinct().ToList() : warnings;

        var receipt = new ParsedReceipt(deduped, detection.Bill, currency, reportedWarnings);
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
