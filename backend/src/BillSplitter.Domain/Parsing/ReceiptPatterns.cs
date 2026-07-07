namespace BillSplitter.Domain.Parsing;

/// <summary>The money-token pattern the parser anchors on, shared so the engine's
/// line-total parse and the multi-line pre-passes cannot drift on what counts as a
/// printed amount. The named groups (<c>neg</c>/<c>sym</c>/<c>whole</c>/<c>frac</c>)
/// feed <see cref="AmountMath"/>; callers compose this with their own anchors, e.g.
/// <c>Money + @"\s*$"</c> for a line-ending total.</summary>
internal static class ReceiptPatterns
{
    // Optional minus and currency symbol, 1-4 whole digits, a '.'/',' and two
    // fraction digits, an optional single-letter VAT-class code ("4.00 B").
    internal const string Money =
        @"(?<neg>-\s*)?(?<sym>[£€$])?\s*(?<whole>\d{1,4})[.,](?<frac>\d{2})(?:\s+[A-Z])?";
}
