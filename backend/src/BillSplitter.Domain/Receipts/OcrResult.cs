namespace BillSplitter.Domain.Receipts;

/// <summary>The sidecar's output: text lines with geometry, ordered top-to-bottom
/// (docs/06-ocr-service.md#post-ocr). Deserialized verbatim from the sidecar's
/// JSON; the field names mirror the wire contract.</summary>
public sealed record OcrResult(int DurationMs, IReadOnlyList<OcrLine> Lines);

public sealed record OcrLine(string Text, double Confidence, OcrBox Box);

/// <summary>Axis-aligned bounding rectangle in pixels of the submitted image.</summary>
public sealed record OcrBox(int X, int Y, int Width, int Height);
