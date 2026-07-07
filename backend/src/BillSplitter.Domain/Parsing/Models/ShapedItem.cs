namespace BillSplitter.Domain.Parsing.Models;

/// <summary>An item row shaped once for the rules to classify: the leading
/// quantity pulled off the cleaned name, that <see cref="Name"/>, and <see
/// cref="Stripped"/> - the name with a reconciling per-unit price column removed
/// (equal to <see cref="Name"/> when there was none). Computed once per row so
/// the competing rules read the same shaping instead of each re-deriving it, which
/// keeps a reject and an item reading from disagreeing about what is left
/// (docs/15-receipt-parsing.md).</summary>
internal readonly record struct ShapedItem(int Quantity, string Name, string Stripped);
