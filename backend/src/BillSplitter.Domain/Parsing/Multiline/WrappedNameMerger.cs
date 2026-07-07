using System.Text.RegularExpressions;

namespace BillSplitter.Domain.Parsing.Multiline;

/// <summary>Joins an item name split across OCR lines onto the priced line that
/// closes it (docs/15-receipt-parsing.md#multi-line): "Classic" / "BAO" / "£6.50"
/// becomes one "Classic BAO £6.50" row. A gated pre-pass run before the pipeline
/// builds candidates - it fires only for a <em>nameless</em> priced line (an
/// amount whose own line carries no name), so every receipt that already prints
/// its name inline is untouched. It also fires only when the receipt has a
/// structural total (a keyword row ending in money), which keeps a non-receipt or
/// a fully column-drifted layout - whose bare prices are the box-sort pass's job,
/// not this one - out of scope. Fragments are borrowed by geometry, not list
/// order: only lines sitting above the price (smaller <c>Box.Y</c>) and in its
/// item's left column (<c>Box.X</c> within a line height of the fragment nearest
/// the price) fold in, so a centred store header or an out-of-order line above the
/// price is flushed as its own line rather than swallowed into the name.</summary>
internal static partial class WrappedNameMerger
{
    /// <summary>Upper bound on how many stacked fragment lines fold into one name -
    /// a safety cap on a runaway column, not the header guard (that is geometric:
    /// the run stops at the first fragment outside the item's left column). Three
    /// covers a name wrapped to three short lines ("Slow-Roasted" / "Pork" /
    /// "Belly").</summary>
    private const int MaxFragments = 3;

    // A line that is only an amount ("£6.50", "6.50", "£ 5.50", "6.50 B"): a price
    // whose name landed on the lines above it. The optional trailing letter is a
    // VAT-class code, so this stays in step with the engine's MoneyAtEnd. Negative
    // amounts are discounts, not wrapped items, so they are excluded.
    [GeneratedRegex(@"^[£€$]?\s*\d{1,4}[.,]\d{2}(?:\s+[A-Z])?\s*$")]
    private static partial Regex NamelessPrice();

    // A keyword total row that still carries its amount ("TOTAL 13.75",
    // "Total: £72.28"): proof the receipt has real structure. Absent it, bare
    // prices are noise (a street sign, a drifted column) we leave alone.
    [GeneratedRegex(@"(TOTAL|AMOUNT|BALANCE|SUBTOTAL|DUE)\b.*\d{1,4}[.,]\d{2}\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex StructuralTotal();

    // A plausible item-name fragment: letters and the joining punctuation names
    // carry, nothing else. Digits, colons and codes ("Table No:1", "1x 3 Courses")
    // are structural, not name text, so they stop a run rather than join it.
    [GeneratedRegex(@"^[A-Za-z][A-Za-z '&-]*$")]
    private static partial Regex NameFragment();

    // Words that are letter-only yet never an item name; a fragment matching one is
    // a header, bill label or payment tender, not the top of a wrapped item. The
    // gate keywords (AMOUNT/BALANCE/DUE) sit here too, so a two-line bill label is
    // never folded into a phantom item.
    private static readonly string[] NonItemWords =
    [
        "TOTAL", "SUBTOTAL", "TAX", "VAT", "GST", "SERVICE", "TIP", "GRATUITY",
        "CASH", "CARD", "CHANGE", "TABLE", "ORDER", "SERVER", "GUEST", "COVER",
        "THANK", "THANKS", "WELCOME", "RECEIPT", "INVOICE", "AMOUNT", "BALANCE",
        "DUE", "PAID", "VISA", "MASTERCARD", "AMEX", "DEBIT", "CREDIT", "MAESTRO",
    ];

    /// <summary>Returns the lines with any wrapped item names folded onto their
    /// price line. The input is unchanged when nothing merges (the common case), so
    /// an already-ordered receipt flows through untouched.</summary>
    public static IReadOnlyList<OcrLine> Merge(IReadOnlyList<OcrLine> lines)
    {
        if (!lines.Any(l => StructuralTotal().IsMatch(l.Text ?? string.Empty)))
        {
            return lines;
        }

        var merged = new List<OcrLine>(lines.Count);
        var fragments = new List<OcrLine>();
        var deferred = new List<OcrLine>();
        foreach (var line in lines)
        {
            var text = (line.Text ?? string.Empty).Trim();

            // A "+ Bacon" note printed between a wrapped name and its price is the
            // modifier pass's job, not a name fragment. Hold it rather than let it
            // break the name mid-assembly, and re-emit it just below the folded
            // price so the modifier pass still folds it into the item.
            if (fragments.Count > 0 && ModifierMerger.IsLeadingAddition(text))
            {
                deferred.Add(line);
                continue;
            }

            if (NamelessPrice().IsMatch(text) && fragments.Count > 0)
            {
                // Fold only the fragments that read as this item's wrapped name -
                // above the price and in its left column. Anything else (a header, a
                // line the OCR emitted out of order) is flushed as its own line.
                var take = WrappedRunLength(fragments, line.Box);
                var flushed = fragments.Count - take;
                for (var i = 0; i < flushed; i++)
                {
                    merged.Add(fragments[i]);
                }

                if (take == 0)
                {
                    // Nothing above the price belongs to it: leave the price alone.
                    merged.Add(line);
                }
                else
                {
                    var name = string.Join(' ', fragments.Skip(flushed).Select(f => f.Text!.Trim()));
                    merged.Add(line with { Text = $"{name} {text}" });
                }

                merged.AddRange(deferred);
                deferred.Clear();
                fragments.Clear();
                continue;
            }

            if (IsFragment(text))
            {
                fragments.Add(line);
                continue;
            }

            merged.AddRange(fragments);
            fragments.Clear();
            merged.AddRange(deferred);
            deferred.Clear();
            merged.Add(line);
        }

        merged.AddRange(fragments);
        merged.AddRange(deferred);
        return merged;
    }

    // The contiguous run of buffered fragments, counting up from the one nearest
    // the price, that fold into its name: each must sit above the price (smaller
    // Box.Y) and share the nearest fragment's left column (Box.X within a line
    // height), bounded by MaxFragments. The column test is what flushes a header
    // stacked above the item instead of swallowing it; the Box.Y test keeps a line
    // the OCR emitted out of vertical order from being folded onto the wrong price.
    private static int WrappedRunLength(List<OcrLine> fragments, OcrBox price)
    {
        var anchor = fragments[^1].Box;
        if (anchor.Y >= price.Y)
        {
            return 0;
        }

        var tolerance = anchor.Height;
        var take = 0;
        for (var i = fragments.Count - 1; i >= 0 && take < MaxFragments; i--)
        {
            var box = fragments[i].Box;
            if (box.Y >= price.Y || Math.Abs(box.X - anchor.X) > tolerance)
            {
                break;
            }

            take++;
        }

        return take;
    }

    private static bool IsFragment(string text) =>
        NameFragment().IsMatch(text)
        && !NonItemWords.Any(w => text.ToUpperInvariant().Split(' ').Contains(w));
}
