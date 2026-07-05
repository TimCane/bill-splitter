namespace BillSplitter.Domain;

/// <summary>OCR job status carried on the session (docs/02-domain-model.md).</summary>
public enum OcrStatus
{
    Pending,
    Processing,
    Done,
    Failed,
}
