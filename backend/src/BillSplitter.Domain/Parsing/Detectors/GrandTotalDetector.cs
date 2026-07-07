using BillSplitter.Domain.Parsing.Classification;
using BillSplitter.Domain.Parsing.Models;

namespace BillSplitter.Domain.Parsing.Detectors;

/// <summary>Locates the receipt's grand total by position, not keyword: of the
/// rows the classifier reads as a total-word row (<see
/// cref="ILineClassifier.IsGrandTotalCandidate"/>), the grand total is the lowest
/// on the page - the largest <c>Box.Y</c>, since grand totals print last - and a
/// same-row tie takes the larger amount (docs/15-receipt-parsing.md#classify-dont-itemise).
/// The result anchors the bill stage: only rows above it are tax/tip/service or
/// items, everything at or below is trailing noise.</summary>
internal sealed class GrandTotalDetector
{
    private readonly ILineClassifier _classifier;

    public GrandTotalDetector(ILineClassifier classifier) => _classifier = classifier;

    public Candidate? Detect(IReadOnlyList<Candidate> candidates)
    {
        Candidate? total = null;
        foreach (var candidate in candidates)
        {
            if (!_classifier.IsGrandTotalCandidate(candidate))
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

        return total;
    }
}
