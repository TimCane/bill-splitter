using BillSplitter.Domain.Parsing.Models;

namespace BillSplitter.Domain.Parsing.Rules;

/// <summary>Runs every <see cref="IReceiptRule"/> against an item row and keeps the
/// highest-confidence <see cref="ItemCandidate"/> (docs/adr/0006-receipt-parser-pipeline.md).
/// Confidences are calibrated so selection reproduces the old first-match cascade:
/// a nameless-price reject outranks a unit-price column, which outranks the default
/// name reading. <see cref="QuantityNamePriceRule"/> always applies, so a winner is
/// guaranteed.</summary>
internal sealed class ItemSelectionEngine
{
    private readonly IReadOnlyList<IReceiptRule> _rules =
    [
        new NamelessPriceRejectRule(),
        new UnitPriceColumnRule(),
        new QuantityNamePriceRule(),
    ];

    public ItemCandidate Select(Candidate candidate)
    {
        ItemCandidate? best = null;
        foreach (var rule in _rules)
        {
            var candidateResult = rule.Apply(candidate);
            if (candidateResult is not null && (best is null || candidateResult.Confidence > best.Confidence))
            {
                best = candidateResult;
            }
        }

        return best!;
    }
}
