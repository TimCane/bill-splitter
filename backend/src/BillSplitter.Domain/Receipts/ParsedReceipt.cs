using BillSplitter.Domain.Sessions;

namespace BillSplitter.Domain.Receipts;

/// <summary>The parser's output: the items and bill to seed a session at review,
/// plus the currency guess and the discard <see cref="Warnings"/> shown to the
/// host (docs/06-ocr-service.md#parsing). Ids are not assigned here - the parser
/// is pure and id minting is the worker's job.</summary>
public sealed record ParsedReceipt(
    IReadOnlyList<ParsedItem> Items,
    Bill Bill,
    string Currency,
    IReadOnlyList<string> Warnings);

/// <summary><see cref="PriceMinor"/> is the printed line total; <see cref="Quantity"/>
/// is informational only (docs/02-domain-model.md#lineitem).</summary>
public sealed record ParsedItem(string Name, int Quantity, long PriceMinor);
