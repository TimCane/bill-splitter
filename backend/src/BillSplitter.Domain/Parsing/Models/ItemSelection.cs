namespace BillSplitter.Domain.Parsing.Models;

/// <summary>The item engine's verdict on one row: the winning <see
/// cref="ItemCandidate"/> and the name of the <c>IReceiptRule</c> that produced it.
/// The rule name feeds the parse-decision trace so a corpus test can assert which
/// reading won a row (docs/15-receipt-parsing.md#diagnostics).</summary>
internal sealed record ItemSelection(ItemCandidate Best, string Rule);
