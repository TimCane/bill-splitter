using BillSplitter.Domain.Parsing.Models;

namespace BillSplitter.Domain.Parsing.Rules;

/// <summary>Shapes an item row once (<see cref="ItemText.Shape"/>), runs every
/// <see cref="IReceiptRule"/> against that shaping and keeps the highest-confidence
/// <see cref="ItemCandidate"/> (docs/adr/0006-receipt-parser-pipeline.md).
/// Confidences are calibrated so selection reproduces the old first-match cascade:
/// a nameless-price reject outranks a unit-price column, which outranks the default
/// name reading. <see cref="QuantityNamePriceRule"/> always applies, so a winner is
/// expected; <see cref="Select"/> throws if a future rule set leaves none rather
/// than returning null into the parse loop.</summary>
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
        var shaped = ItemText.Shape(candidate);
        ItemCandidate? best = null;
        foreach (var rule in _rules)
        {
            var result = rule.Apply(candidate, shaped);
            if (result is not null && (best is null || result.Confidence > best.Confidence))
            {
                best = result;
            }
        }

        return best ?? throw new InvalidOperationException(
            "no item rule produced a reading; the rule set must include an always-applies default");
    }
}
