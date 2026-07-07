using BillSplitter.Domain.Parsing.Models;

namespace BillSplitter.Domain.Parsing.Rules;

/// <summary>Rejects a priced row whose name shapes away to nothing - a price with
/// no recoverable label ("2 5.00" where the whole name is a count and its unit
/// price). Shapes the row exactly as the item rules would (clean, quantity, then
/// unit-price column) and, if nothing is left, parks a warning at the highest
/// confidence so it outranks any item reading of the same row
/// (docs/15-receipt-parsing.md#price-detection).</summary>
internal sealed class NamelessPriceRejectRule : IReceiptRule
{
    public const int Confidence = 100;

    public ItemCandidate? Apply(Candidate candidate)
    {
        var (quantity, name) = ItemText.ExtractQuantity(ItemText.CleanName(candidate.Name));
        name = ItemText.StripUnitPrice(name, candidate.Amount, quantity);
        return name.Length == 0
            ? ItemCandidate.Reject(
                Confidence, $"price with no name ignored: {candidate.Text}", nameof(NamelessPriceRejectRule))
            : null;
    }
}
