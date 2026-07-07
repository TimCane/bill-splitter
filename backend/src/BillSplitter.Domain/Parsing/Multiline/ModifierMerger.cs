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
    // A money token (ReceiptPatterns.Money) at the end of the line - the same shape
    // the engine anchors on, shared so the two cannot disagree about where a price
    // starts.
    [GeneratedRegex(ReceiptPatterns.Money + @"\s*$")]
    private static partial Regex MoneyAtEnd();

    // The whole trailing numeric tail - a per-unit column and the line total run
    // together ("2.00 4.00" in "2 BREAD 2.00 4.00"). The modifier is spliced ahead
    // of this run, not just the last token, so a reconciling unit column stays
    // trailing for the item rules to drop.
    [GeneratedRegex(@"(?:[£€$]?\s*-?\d{1,4}[.,]\d{2}(?:\s+[A-Z])?\s*)+$")]
    private static partial Regex TrailingMoneyRun();

    // Whitespace-collapsed form used only to test the modifier shape.
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    // A leading addition: "+ Bacon", "+Bacon", "*Extra Cheese". Bounded to one or
    // two words like KeywordModifier - a longer "* 12.5% service charge added for
    // large parties" footnote is a disclaimer, not a modifier, and must not fold
    // into the item above.
    [GeneratedRegex(@"^[+*]\s*\S+(?:\s+\S+)?$")]
    private static partial Regex LeadingAddition();

    // A short instruction modifier: a leading keyword and one or two following
    // words. The end anchor keeps it short; longer sentences are not modifiers.
    [GeneratedRegex(
        @"^(?:NO|EXTRA|ADD|HOLD|SUB|LESS|W/O?|WITHOUT)\s+\S+(?:\s+\S+)?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex KeywordModifier();

    // Words that name a bill line or a payment/status footer rather than an item
    // note - "No payment received", "Add Gratuity", "Less Service", "Add Tax". A
    // modifier carrying one of these must not fold: the enriched item name would
    // then contain the classifier's own keyword and the whole row would read as
    // that tip/service/tax line and drop out of the split.
    private static readonly HashSet<string> ExcludedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "payment", "received", "change", "served", "charge",
        "total", "subtotal", "balance", "due",
        "tip", "tax", "gratuity", "service", "vat", "gst", "discount", "fee",
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

            var money = MoneyAtEnd().Match(text);
            if (money.Success)
            {
                merged?.Add(line);
                // Only a priced line that carries its own name can take a modifier.
                // A bare price is one the wrapped-name pass ran before us left
                // nameless; attaching a note to it would name a row the reject rule
                // is about to drop, manufacturing a phantom item.
                var hasName = text[..money.Index].Trim().Length > 0;
                attachIndex = hasName ? (merged?.Count ?? i + 1) - 1 : -1;
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

    /// <summary>A leading "+"/"*" addition ("+ Bacon", "* Extra Cheese") - the
    /// unambiguous modifier form, never a wrapped-name fragment. The wrapped-name
    /// pass steps over such a line without breaking a name it is assembling, so it
    /// survives for <see cref="Merge"/> to fold into the item below.</summary>
    internal static bool IsLeadingAddition(string text)
    {
        var collapsed = Whitespace().Replace(text.Trim(), " ");
        return LeadingAddition().IsMatch(collapsed) && IsModifier(collapsed);
    }

    private static bool IsModifier(string text)
    {
        if (!LeadingAddition().IsMatch(text) && !KeywordModifier().IsMatch(text))
        {
            return false;
        }

        // Applies to both shapes: a "* Service" or "Add Gratuity" line matches the
        // modifier form but names a bill extra, so it is left for the classifier.
        foreach (var word in text.Split(' '))
        {
            if (ExcludedWords.Contains(word.Trim('+', '*', '.', ',', ':')))
            {
                return false;
            }
        }

        return true;
    }

    private static OcrLine Attach(OcrLine priced, string modifier)
    {
        var tail = TrailingMoneyRun().Match(priced.Text);
        if (!tail.Success)
        {
            return priced; // defensive: the priced line always carries an amount
        }

        var name = priced.Text[..tail.Index].TrimEnd();
        return priced with { Text = $"{name} {modifier} {priced.Text[tail.Index..]}" };
    }
}
