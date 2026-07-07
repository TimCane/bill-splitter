using BillSplitter.Domain.Parsing.Models;

namespace BillSplitter.Domain.Parsing.Rules;

/// <summary>One competing reading of a priced item row
/// (docs/adr/0006-receipt-parser-pipeline.md, docs/15-receipt-parsing.md). The
/// engine shapes the row once (<see cref="ShapedItem"/>) and hands every rule the
/// same shaping; each rule inspects it and either returns its <see
/// cref="ItemCandidate"/> - a parsed item or a reject, carrying the confidence the
/// engine ranks it by - or <c>null</c> when the row does not fit its layout. The
/// engine keeps the highest-confidence candidate rather than the first rule to
/// match.</summary>
internal interface IReceiptRule
{
    ItemCandidate? Apply(Candidate candidate, ShapedItem shaped);
}
