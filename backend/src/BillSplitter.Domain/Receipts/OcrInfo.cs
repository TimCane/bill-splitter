using System.Text.Json.Serialization;

namespace BillSplitter.Domain.Receipts;

/// <summary>OCR status, (host-only) failure reason and the parser's discard
/// <see cref="Warnings"/> shown to the host under the checksum banner
/// (docs/02-domain-model.md#ocrinfo, docs/09-ux-flows.md#4-review-host-gate---state-review).</summary>
public sealed class OcrInfo
{
    private List<string> _warnings;

    [JsonConstructor]
    public OcrInfo(OcrStatus status, string? failureReason, IReadOnlyList<string>? warnings = null)
    {
        Status = status;
        FailureReason = failureReason;
        _warnings = warnings is null ? [] : [.. warnings];
    }

    public OcrStatus Status { get; private set; }

    public string? FailureReason { get; private set; }

    public IReadOnlyList<string> Warnings => _warnings;

    internal void Set(OcrStatus status, string? failureReason)
    {
        Status = status;
        FailureReason = failureReason;
    }

    internal void SetWarnings(IEnumerable<string> warnings) => _warnings = [.. warnings];

    /// <summary>Drop the host-only parse detail (failure reason and discard
    /// warnings) that only drives the Review gate, so it never rides the shared
    /// snapshot once non-hosts can read it (docs/02-domain-model.md#ocrinfo).</summary>
    internal void ClearHostOnlyDetail()
    {
        FailureReason = null;
        _warnings = [];
    }
}
