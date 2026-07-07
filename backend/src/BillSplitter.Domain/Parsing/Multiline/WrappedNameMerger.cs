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
/// not this one - out of scope.</summary>
internal static partial class WrappedNameMerger
{
    /// <summary>How many stacked fragment lines above a price we will fold into
    /// one name. A wrapped name runs to two short lines ("Large Double" / "Bacon");
    /// bounding the run keeps a header stacked above the item from being swallowed -
    /// it is flushed as its own line instead.</summary>
    private const int MaxFragments = 2;

    // A line that is only an amount ("£6.50", "6.50", "£ 5.50"): a price whose name
    // landed on the lines above it. Negative amounts are discounts, not wrapped
    // items, so they are excluded.
    [GeneratedRegex(@"^[£€$]?\s*\d{1,4}[.,]\d{2}\s*$")]
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
    // a header or bill label, not the top of a wrapped item.
    private static readonly string[] NonItemWords =
    [
        "TOTAL", "SUBTOTAL", "TAX", "VAT", "GST", "SERVICE", "TIP", "GRATUITY",
        "CASH", "CARD", "CHANGE", "TABLE", "ORDER", "SERVER", "GUEST", "COVER",
        "THANK", "THANKS", "WELCOME", "RECEIPT", "INVOICE",
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
        foreach (var line in lines)
        {
            var text = (line.Text ?? string.Empty).Trim();

            if (NamelessPrice().IsMatch(text) && fragments.Count > 0)
            {
                // Fold the run immediately above (bounded, so a header two lines up
                // is emitted as its own line rather than swallowed) onto the price.
                var take = Math.Min(MaxFragments, fragments.Count);
                var flushed = fragments.Count - take;
                for (var i = 0; i < flushed; i++)
                {
                    merged.Add(fragments[i]);
                }

                var name = string.Join(' ', fragments.Skip(flushed).Select(f => f.Text!.Trim()));
                merged.Add(line with { Text = $"{name} {text}" });
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
            merged.Add(line);
        }

        merged.AddRange(fragments);
        return merged;
    }

    private static bool IsFragment(string text) =>
        NameFragment().IsMatch(text)
        && !NonItemWords.Any(w => text.ToUpperInvariant().Split(' ').Contains(w));
}
