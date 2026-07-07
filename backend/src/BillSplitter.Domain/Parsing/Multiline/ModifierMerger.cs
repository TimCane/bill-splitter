using System.Text.RegularExpressions;

namespace BillSplitter.Domain.Parsing.Multiline;

/// <summary>
/// A pre-pass over the OCR lines that folds modifier lines - amount-less notes
/// like <c>+ Bacon</c>, <c>No Onion</c> or <c>Extra Sauce</c> that qualify the
/// item above them - into the preceding priced line before the engine builds its
/// candidates (docs/15-receipt-parsing.md#multi-line). The modifier text is
/// spliced in ahead of the money token, so the existing pipeline reads one
/// enriched item ("Burger" + "+ Bacon" -> "Burger + Bacon") and no separate row.
///
/// Deliberately conservative: only a leading <c>+</c>/<c>*</c> addition or a short
/// keyword form (NO/EXTRA/ADD/... plus one or two words) attaches, and only when
/// the line sits directly below a priced line. Amount-less headers, addresses,
/// section labels and payment-status footers such as "No payment received" are
/// left untouched so the rest of the pipeline sees them exactly as before.
/// </summary>
internal static partial class ModifierMerger
{
    // A money token at the end of the line - the same shape the engine anchors on.
    // The modifier text is inserted immediately before this token.
    [GeneratedRegex(@"(?<neg>-\s*)?(?<sym>[£€$])?\s*(?<whole>\d{1,4})[.,](?<frac>\d{2})(?:\s+[A-Z])?\s*$")]
    private static partial Regex MoneyAtEnd();

    // Whitespace-collapsed form used only to test the modifier shape.
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    // A leading addition: "+ Bacon", "+Bacon", "*Extra Cheese".
    [GeneratedRegex(@"^[+*]\s*\S")]
    private static partial Regex LeadingAddition();

    // A short instruction modifier: a leading keyword and one or two following
    // words. The end anchor keeps it short; longer sentences are not modifiers.
    [GeneratedRegex(
        @"^(?:NO|EXTRA|ADD|HOLD|SUB|LESS|W/O?|WITHOUT)\s+\S+(?:\s+\S+)?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex KeywordModifier();

    // Words that turn a keyword line into a payment or status footer, not a
    // modifier - "No payment received", "No change due", "Less balance", etc.
    private static readonly HashSet<string> StatusWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "payment", "received", "change", "served", "charge",
        "total", "subtotal", "balance", "due",
    };

    /// <summary>Returns the lines with any modifier lines merged into the priced
    /// line above them and dropped. Non-modifier lines pass through untouched, in
    /// order; if nothing merges the original list is returned.</summary>
    public static IReadOnlyList<OcrLine> Merge(IReadOnlyList<OcrLine> lines)
    {
        List<OcrLine>? merged = null;
        var attachIndex = -1; // index into `merged` of the priced line to attach to

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var text = Whitespace().Replace((line.Text ?? string.Empty).Trim(), " ");

            if (MoneyAtEnd().IsMatch(text))
            {
                merged?.Add(line);
                attachIndex = (merged?.Count ?? i + 1) - 1;
                continue;
            }

            if (attachIndex >= 0 && IsModifier(text))
            {
                merged ??= new List<OcrLine>(lines.Take(i));
                merged[attachIndex] = Attach(merged[attachIndex], text);
                continue; // keep attachIndex so further modifiers chain onto the item
            }

            // A non-modifier amount-less line: keep it, and break the adjacency so a
            // later modifier does not reach back past it.
            merged?.Add(line);
            attachIndex = -1;
        }

        return merged ?? lines;
    }

    private static bool IsModifier(string text)
    {
        if (LeadingAddition().IsMatch(text))
        {
            return true;
        }

        if (!KeywordModifier().IsMatch(text))
        {
            return false;
        }

        foreach (var word in text.Split(' '))
        {
            if (StatusWords.Contains(word.Trim('.', ',', ':')))
            {
                return false;
            }
        }

        return true;
    }

    private static OcrLine Attach(OcrLine priced, string modifier)
    {
        var money = MoneyAtEnd().Match(priced.Text);
        if (!money.Success)
        {
            return priced; // defensive: the priced line always carries an amount
        }

        var name = priced.Text[..money.Index].TrimEnd();
        var amount = priced.Text[money.Index..];
        return priced with { Text = $"{name} {modifier} {amount}" };
    }
}
