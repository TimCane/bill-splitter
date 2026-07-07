using System.Text.RegularExpressions;
using BillSplitter.Domain.Parsing.Classification;
using BillSplitter.Domain.Parsing.Models;

namespace BillSplitter.Domain.Parsing.Detectors;

/// <summary>Reads the bill fields off the priced candidates
/// (docs/15-receipt-parsing.md#classify-dont-itemise). It anchors on the grand
/// total (<see cref="GrandTotalDetector"/>), then classifies only the rows above
/// it: tax, tip and service become bill extras; subtotals, category rollups, VAT
/// breakdowns, intermediate totals and payment noise are dropped as amounts we
/// recompute or never owe; a bare charge is parked; everything else is an item row
/// for the item engine. Everything at or below the grand total is trailing noise
/// the receipt prints after the amount due. Keyword classification lives in the
/// <see cref="ILineClassifier"/> (extracted in A3); this stage composes it with the
/// positional grand-total anchor to build the <see cref="Bill"/>.</summary>
internal sealed partial class BillDetectionEngine
{
    private readonly ILineClassifier _classifier;
    private readonly GrandTotalDetector _grandTotal;

    public BillDetectionEngine(ILineClassifier classifier)
    {
        _classifier = classifier;
        _grandTotal = new GrandTotalDetector(classifier);
    }

    // A per-unit detail line printed under its item ("2 @ $35.50"): once its own
    // price is stripped the name is only "2 @", so the whole name is a count and
    // an "@" (with any leftover digits). An item like "2 @ 6.40 Miso Soup" has a
    // real name after and is not matched.
    [GeneratedRegex(@"^\d+\s*@\s*[£€$]?[\d.,]*\s*$")]
    private static partial Regex UnitPriceDetail();

    public BillDetection Detect(IReadOnlyList<Candidate> candidates)
    {
        var total = _grandTotal.Detect(candidates);
        var totalY = total?.Y ?? int.MaxValue;

        // Classify only what sits above the grand total. Everything at or below it
        // - VAT breakdowns, payment lines, "divide by N" hints - is trailing noise
        // the receipt prints after the amount due, not tax/tip/service or items.
        var warnings = new List<string>();
        var decisions = new List<ParseDecision>();
        long tax = 0, tip = 0, service = 0;
        var itemRows = new List<Candidate>();
        foreach (var candidate in candidates)
        {
            if (ReferenceEquals(candidate, total))
            {
                decisions.Add(Decide(candidate, LineType.Total, "GrandTotalDetector",
                    "grand total: lowest total-word row by Box.Y"));
                continue;
            }

            if (candidate.Y >= totalY)
            {
                decisions.Add(Decide(candidate, LineType.Unknown, nameof(BillDetectionEngine),
                    "trailing noise at or below the grand total"));
                continue;
            }

            var type = _classifier.Classify(candidate);
            if (type is LineType.Subtotal or LineType.ItemCount
                or LineType.CategoryRollup or LineType.TaxBreakdown)
            {
                // Subtotals, rollups and VAT breakdowns: we compute our own.
                decisions.Add(Decide(candidate, type, nameof(KeywordClassifier), "dropped: an amount we recompute"));
                continue;
            }

            if (type == LineType.Tax)
            {
                tax = candidate.Amount;
                decisions.Add(Decide(candidate, type, nameof(KeywordClassifier), "tax keyword"));
            }
            else if (type == LineType.Tip)
            {
                tip = candidate.Amount;
                decisions.Add(Decide(candidate, type, nameof(KeywordClassifier), "tip keyword"));
            }
            else if (type == LineType.Service)
            {
                service = candidate.Amount;
                decisions.Add(Decide(candidate, type, nameof(KeywordClassifier), "service keyword or lone label above amount"));
            }
            else if (type == LineType.Total)
            {
                // An intermediate total ("Item Total"): we compute our own.
                decisions.Add(Decide(candidate, type, nameof(KeywordClassifier), "intermediate total: recomputed"));
            }
            else if (type == LineType.Payment)
            {
                // Payment tender lines: ignore.
                decisions.Add(Decide(candidate, type, nameof(KeywordClassifier), "payment tender line"));
            }
            else if (UnitPriceDetail().IsMatch(candidate.Name))
            {
                // "2 @ $35.50" under its item: the item already has the line total.
                decisions.Add(Decide(candidate, type, nameof(BillDetectionEngine), "per-unit detail printed under its item"));
            }
            else if (type == LineType.BareCharge)
            {
                // A bare amount with no recoverable label - a columnar misread.
                warnings.Add($"unreadable amount ignored: {candidate.Text}");
                decisions.Add(Decide(candidate, type, nameof(KeywordClassifier), "bare amount with no recoverable label"));
            }
            else
            {
                // An item row: the item engine decides and traces it, not this stage.
                itemRows.Add(candidate);
            }
        }

        var bill = new Bill(tax, tip, service, total?.Amount ?? 0);
        return new BillDetection(bill, itemRows, warnings, decisions);
    }

    private static ParseDecision Decide(Candidate candidate, LineType type, string rule, string evidence) =>
        new(candidate.Text, type, 0, rule, [evidence]);
}
