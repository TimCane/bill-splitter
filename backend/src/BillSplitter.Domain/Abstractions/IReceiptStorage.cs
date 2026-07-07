namespace BillSplitter.Domain.Abstractions;

/// <summary>A stored receipt image and the content type to echo back
/// (docs/04-api-contract.md#get-apiv1sessionssessionidreceipt).</summary>
public sealed record ReceiptObject(byte[] Content, string ContentType);

/// <summary>
/// Transient store for the receipt image. The object lives only from create until
/// the host opens the session, then it is deleted (docs/01-architecture.md#receipt-image-lifecycle).
/// </summary>
public interface IReceiptStorage
{
    Task PutAsync(string sessionId, byte[] content, string contentType, CancellationToken ct);

    /// <summary>Fetch the stored image, or null if it is gone (deleted at open).</summary>
    Task<ReceiptObject?> GetAsync(string sessionId, CancellationToken ct);

    Task DeleteAsync(string sessionId, CancellationToken ct);
}
