namespace BillSplitter.Domain.Parsing.Models;

/// <summary>The parse plus its in-memory decision trace: the public <see
/// cref="ParsedReceipt"/> the facade returns, and the <see cref="Trace"/> of why
/// each priced line was classified. The trace is a test-only diagnostic surface -
/// it never rides the wire contract and is never logged, so no receipt text leaves
/// the process (docs/10-security-privacy.md, docs/15-receipt-parsing.md#diagnostics).
/// <c>ReceiptParser.Parse</c> discards it; only the corpus tests read it.</summary>
internal sealed record TracedReceipt(ParsedReceipt Receipt, IReadOnlyList<ParseDecision> Trace);
