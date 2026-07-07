using BillSplitter.Domain.Receipts;

namespace BillSplitter.Domain.Abstractions;

/// <summary>Calls the OCR sidecar: image bytes in, recognized lines out
/// (docs/06-ocr-service.md#post-ocr). The <paramref name="contentType"/> is the
/// stored image's type so the sidecar echoes the right decode path.</summary>
public interface IOcrClient
{
    Task<OcrResult> RecognizeAsync(Stream image, string contentType, CancellationToken ct);
}
