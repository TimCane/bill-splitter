using BillSplitter.Domain.Parsing.Engine;

namespace BillSplitter.Domain;

/// <summary>
/// Pure OCR-lines-to-<see cref="ParsedReceipt"/> parsing
/// (docs/06-ocr-service.md#parsing). A thin static facade over the rule/engine
/// pipeline (docs/adr/0006-receipt-parser-pipeline.md) so the one call site
/// (<c>OcrWorker</c>) and the no-DI, pure-Domain status quo are unchanged. The
/// fixture corpus is the real spec (docs/11-testing-strategy.md#receiptparser).
/// </summary>
public static class ReceiptParser
{
    public static ParsedReceipt Parse(OcrResult result) => ReceiptParseEngine.Parse(result);
}
