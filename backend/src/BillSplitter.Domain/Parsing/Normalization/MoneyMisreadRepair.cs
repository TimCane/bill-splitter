using System.Text;
using System.Text.RegularExpressions;

namespace BillSplitter.Domain.Parsing.Normalization;

/// <summary>Repairs the handful of OCR letter/digit confusions that only ever
/// occur inside a price - <c>O</c>/<c>0</c>, <c>S</c>->5, <c>l</c>/<c>I</c>->1,
/// <c>B</c>->8, and a leading <c>E</c>->£ - so <c>E12.5O</c> reads as
/// <c>£12.50</c> (docs/15-receipt-parsing.md#normalization). Deliberately not
/// part of <see cref="BasicNormalizer"/>: this is a targeted money-span fix, not
/// generic tidying, so it is gated hard to a trailing money-shaped token that
/// carries at least one real digit and at least one repairable glyph. Alphabetic
/// item names (<c>7UP</c>, <c>No.8 Burger</c>, <c>Coke Zero</c>, <c>KX BOB</c>)
/// have no such token and pass through untouched.</summary>
internal static partial class MoneyMisreadRepair
{
    // A trailing money-shaped token that mixes real digits with repairable
    // glyphs. Whole and fraction runs accept digits plus O/S/l/I/B; an optional
    // leading currency symbol may be a misread E (attached, no space) - but only
    // when it is not the tail of a word, so a name glued to its price ("WINE12.00",
    // "PALE ALE8.00") keeps its final letter instead of losing it to a phantom £.
    // The trailing VAT-class letter is matched but never rewritten - "4.00 B" keeps
    // its code.
    [GeneratedRegex(@"(?:(?<sym>[£€$])\s*|(?<sym>(?<![A-Za-z])E))?(?<whole>[\dOSlIB]{1,4})(?<sep>[.,])(?<frac>[\dOSlIB]{2})(?<vat>\s+[A-Z])?\s*$")]
    private static partial Regex MoneyToken();

    public static string Repair(string text)
    {
        var match = MoneyToken().Match(text);
        if (!match.Success)
        {
            return text;
        }

        var sym = match.Groups["sym"].Value;
        var whole = match.Groups["whole"].Value;
        var frac = match.Groups["frac"].Value;
        var leadingE = sym == "E";

        // Gate: only repair a token that is genuinely a misread price - it must
        // hold at least one real digit and at least one thing to fix (a repairable
        // letter in the number, or a leading E standing in for £). Clean money and
        // stray letter-only text are left for the money regex to accept or reject.
        var hasDigit = HasDigit(whole) || HasDigit(frac);
        var hasRepair = leadingE || HasLetter(whole) || HasLetter(frac);
        if (!hasDigit || !hasRepair)
        {
            return text;
        }

        var token = new StringBuilder();
        token.Append(leadingE ? "£" : sym);
        RepairInto(token, whole);
        token.Append(match.Groups["sep"].Value);
        RepairInto(token, frac);
        token.Append(match.Groups["vat"].Value);

        return string.Concat(text.AsSpan(0, match.Index), token.ToString());
    }

    private static void RepairInto(StringBuilder builder, string span)
    {
        foreach (var ch in span)
        {
            builder.Append(ch switch
            {
                'O' => '0',
                'S' => '5',
                'l' or 'I' => '1',
                'B' => '8',
                _ => ch,
            });
        }
    }

    private static bool HasDigit(string span)
    {
        foreach (var ch in span)
        {
            if (ch is >= '0' and <= '9')
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasLetter(string span)
    {
        foreach (var ch in span)
        {
            if (ch is 'O' or 'S' or 'l' or 'I' or 'B')
            {
                return true;
            }
        }

        return false;
    }
}
