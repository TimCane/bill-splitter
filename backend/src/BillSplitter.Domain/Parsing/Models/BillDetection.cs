namespace BillSplitter.Domain.Parsing.Models;

/// <summary>What the bill-detection stage read off a receipt: the <see cref="Bill"/>
/// (grand total plus the tax/tip/service extras) and the rows left over as items
/// for the item engine, with any warnings the stage parked on the way
/// (docs/15-receipt-parsing.md#classify-dont-itemise). The engine partitions the
/// priced candidates so the item engine never sees a bill line and the bill never
/// counts an item.</summary>
internal sealed record BillDetection(
    Bill Bill, IReadOnlyList<Candidate> ItemRows, IReadOnlyList<string> Warnings);
