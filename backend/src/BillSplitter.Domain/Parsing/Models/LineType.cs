namespace BillSplitter.Domain.Parsing.Models;

/// <summary>What a receipt line was classified as. The classifier walks its
/// rules in priority order and the highest-priority match wins
/// (docs/15-receipt-parsing.md#classify-dont-itemise).</summary>
internal enum LineType
{
    /// <summary>No rule claimed the line - a header, address or free text.</summary>
    Unknown = 0,
    Item,
    Subtotal,
    Total,
    Tax,

    /// <summary>A VAT breakdown summary that pairs a tax word with a `%` rate
    /// ("20% VAT Total 21.99") - a report of tax already in the total, not
    /// payable tax. Dropped, never added to <c>bill.taxMinor</c>.</summary>
    TaxBreakdown,
    Tip,
    Service,
    Payment,
    CategoryRollup,
    ItemCount,

    /// <summary>A bare amount whose name carries no item - a stray percentage or
    /// the columnar residue of a split charge line. Parked in <c>Warnings</c>.</summary>
    BareCharge,
}
