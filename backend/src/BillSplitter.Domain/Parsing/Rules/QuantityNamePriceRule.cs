using BillSplitter.Domain.Parsing.Models;

namespace BillSplitter.Domain.Parsing.Rules;

/// <summary>The default item reading: clean the name, pull a leading quantity off
/// it, and keep the row's printed amount as the line total ("2 Burger 10.00" ->
/// qty 2, "Burger"). Always applies, at the lowest confidence, so any more
/// specific rule (a unit-price column, a nameless reject) outranks it
/// (docs/15-receipt-parsing.md#item-line-catalogue).</summary>
internal sealed class QuantityNamePriceRule : IReceiptRule
{
    public const int Confidence = 30;

    public ItemCandidate? Apply(Candidate candidate, ShapedItem shaped) =>
        ItemCandidate.ForItem(
            Confidence,
            new ParsedItem(shaped.Name, shaped.Quantity, candidate.Amount),
            $"name reading, quantity {shaped.Quantity}");
}
