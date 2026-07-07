using System.Text.RegularExpressions;

namespace BillSplitter.Domain.Parsing.Normalization;

/// <summary>The default <see cref="ITextNormalizer"/>: trim surrounding
/// whitespace and collapse internal runs to a single space. Nothing
/// item-specific - just the shared "tidy the raw line" step every line gets.</summary>
internal sealed partial class BasicNormalizer : ITextNormalizer
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public string Normalize(string text) =>
        Whitespace().Replace((text ?? string.Empty).Trim(), " ");
}
