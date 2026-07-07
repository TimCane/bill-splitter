namespace BillSplitter.Domain.Parsing.Models;

/// <summary>What a receipt line was classified as. The engine keeps the
/// highest-confidence classification per line rather than the first match
/// (docs/15-receipt-parsing.md#classify-dont-itemise).</summary>
internal enum LineType
{
    /// <summary>No rule claimed the line - a header, address or free text.</summary>
    Unknown = 0,
    Item,
    Subtotal,
    Total,
    Tax,
    Tip,
    Service,
    Payment,
    CategoryRollup,
    ItemCount,
    Discount,
}
