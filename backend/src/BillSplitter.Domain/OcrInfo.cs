using System.Text.Json.Serialization;

namespace BillSplitter.Domain;

/// <summary>OCR status and (host-only) failure reason
/// (docs/02-domain-model.md#ocrinfo).</summary>
public sealed class OcrInfo
{
    [JsonConstructor]
    public OcrInfo(OcrStatus status, string? failureReason)
    {
        Status = status;
        FailureReason = failureReason;
    }

    public OcrStatus Status { get; private set; }

    public string? FailureReason { get; private set; }

    internal void Set(OcrStatus status, string? failureReason)
    {
        Status = status;
        FailureReason = failureReason;
    }
}
