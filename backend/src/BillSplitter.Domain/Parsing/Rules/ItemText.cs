using System.Globalization;
using System.Text.RegularExpressions;
using BillSplitter.Domain.Parsing.Models;

namespace BillSplitter.Domain.Parsing.Rules;

/// <summary>Shapes an item row once into the <see cref="ShapedItem"/> the rules
/// classify (docs/15-receipt-parsing.md): strip codes and unit-price noise from
/// the name, pull a leading quantity off it, and drop a reconciling per-unit price
/// column. The rules read the result; they never re-derive it, so a reject and an
/// item reading of the same row cannot disagree about what is left.</summary>
internal static partial class ItemText
{
    // A bare money token at the end of a name, e.g. the per-unit column in
    // "2 BREAD 2.00 4.00" once the line total is removed. A deliberate variant of
    // ReceiptPatterns.Money with its own uwhole/ufrac groups for the unit-price
    // arithmetic and no negative branch (a discount is never a per-unit column).
    [GeneratedRegex(@"(?<sym>[£€$])?\s*(?<uwhole>\d{1,4})[.,](?<ufrac>\d{2})\s*$")]
    private static partial Regex TrailingMoney();

    // A leading quantity: "2 ", "2x ", "3 X " -> the count, stripped from the name.
    [GeneratedRegex(@"^(?<qty>\d{1,2})\s?[xX]?\s+")]
    private static partial Regex LeadingQuantity();

    // A "#12"-style item code, dropped from the name.
    [GeneratedRegex(@"#\S+")]
    private static partial Regex ItemCode();

    // A "@ 6.50" per-unit price annotation printed alongside the line total
    // ("5 CLASSIC BAO @6.50 ... 32.50"). Noise in the name; the row's own amount
    // is already the line total.
    [GeneratedRegex(@"@\s*[£€$]?\d{1,4}[.,]\d{2}")]
    private static partial Regex AtUnitPrice();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static ShapedItem Shape(Candidate candidate)
    {
        var (quantity, name) = ExtractQuantity(CleanName(candidate.Name));
        var stripped = StripUnitPrice(name, candidate.Amount, quantity);
        return new ShapedItem(quantity, name, stripped);
    }

    private static string CleanName(string raw)
    {
        var name = ItemCode().Replace(raw, string.Empty);
        name = AtUnitPrice().Replace(name, string.Empty); // strip "@ 6.50" unit prices
        name = name.Trim().TrimEnd('.', ' ', '-', '=', '@'); // strip dot leaders and separators
        return Whitespace().Replace(name, " ").Trim();
    }

    private static (int Quantity, string Name) ExtractQuantity(string name)
    {
        var match = LeadingQuantity().Match(name);
        if (!match.Success)
        {
            return (1, name);
        }

        var rest = name[match.Length..].Trim();
        var quantity = int.Parse(match.Groups["qty"].Value, CultureInfo.InvariantCulture);
        return rest.Length > 0 && quantity >= 1 ? (quantity, rest) : (1, name);
    }

    // Drop the per-unit price column ("2 ROAST BEEF 27.00" -> "ROAST BEEF") only
    // when it is unambiguous: a multiple quantity whose trailing number times the
    // count equals the line total. A single item's trailing number is left alone.
    private static string StripUnitPrice(string name, long amount, int quantity)
    {
        if (quantity <= 1)
        {
            return name;
        }

        var match = TrailingMoney().Match(name);
        if (!match.Success)
        {
            return name;
        }

        var unit = AmountMath.ToMinorUnits(match.Groups["uwhole"].Value, match.Groups["ufrac"].Value);
        return unit * quantity == amount
            ? name[..match.Index].Trim().TrimEnd('.', ' ', '-')
            : name;
    }
}
