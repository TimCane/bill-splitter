namespace BillSplitter.Domain.Parsing.Normalization;

/// <summary>Generic line-level text normalization run on every OCR line before
/// the pipeline classifies it (docs/06-ocr-service.md#parsing). Deliberately
/// item-agnostic: item-name concerns like <c>#code</c>/<c>@unit</c> stripping
/// and OCR-misread fixing are separate rules, not normalization.</summary>
internal interface ITextNormalizer
{
    string Normalize(string text);
}
