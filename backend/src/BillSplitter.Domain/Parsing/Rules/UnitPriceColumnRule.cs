using BillSplitter.Domain.Parsing.Models;
using BillSplitter.Domain.Receipts;

namespace BillSplitter.Domain.Parsing.Rules;

/// <summary>A "qty name unit-price line-total" row ("2 ROAST BEEF 27.00 54.00"):
/// when the quantity is a multiple and a trailing per-unit price times the count
/// equals the line total, drop the column and keep just the name. Outranks
/// <see cref="QuantityNamePriceRule"/>, so when the column reconciles the stripped
/// name wins; when it does not, this rule stands aside and the default reading
/// keeps the trailing number (docs/15-receipt-parsing.md#unit-price).</summary>
internal sealed class UnitPriceColumnRule : IReceiptRule
{
    public const int Confidence = 60;

    public ItemCandidate? Apply(Candidate candidate, ShapedItem shaped)
    {
        if (shaped.Stripped == shaped.Name)
        {
            // No reconciling per-unit column to drop: leave the row to the default rule.
            return null;
        }

        return ItemCandidate.ForItem(
            Confidence,
            new ParsedItem(shaped.Stripped, shaped.Quantity, candidate.Amount),
            $"dropped reconciling per-unit column, quantity {shaped.Quantity}");
    }
}
