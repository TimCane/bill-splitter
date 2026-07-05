using BillSplitter.Domain;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace BillSplitter.Infrastructure.Storage;

/// <summary>
/// MinIO-backed <see cref="IReceiptStorage"/>. Objects live at
/// <c>receipts/{sessionId}</c> in the configured bucket with the sniffed image
/// content type (docs/07-backend-design.md#infrastructure-project).
/// </summary>
public sealed class MinioReceiptStorage : IReceiptStorage
{
    private readonly IMinioClient _client;
    private readonly string _bucket;

    public MinioReceiptStorage(IMinioClient client, string bucket)
    {
        _client = client;
        _bucket = bucket;
    }

    public async Task PutAsync(string sessionId, byte[] content, string contentType, CancellationToken ct)
    {
        await EnsureBucketAsync(ct);

        using var stream = new MemoryStream(content, writable: false);
        await _client.PutObjectAsync(
            new PutObjectArgs()
                .WithBucket(_bucket)
                .WithObject(ObjectKey(sessionId))
                .WithStreamData(stream)
                .WithObjectSize(content.LongLength)
                .WithContentType(contentType),
            ct);
    }

    public async Task<ReceiptObject?> GetAsync(string sessionId, CancellationToken ct)
    {
        var key = ObjectKey(sessionId);
        try
        {
            var stat = await _client.StatObjectAsync(
                new StatObjectArgs().WithBucket(_bucket).WithObject(key),
                ct);

            using var buffer = new MemoryStream();
            await _client.GetObjectAsync(
                new GetObjectArgs()
                    .WithBucket(_bucket)
                    .WithObject(key)
                    .WithCallbackStream((stream, token) => stream.CopyToAsync(buffer, token)),
                ct);

            return new ReceiptObject(buffer.ToArray(), stat.ContentType);
        }
        catch (Exception ex) when (ex is ObjectNotFoundException or BucketNotFoundException)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string sessionId, CancellationToken ct)
    {
        try
        {
            await _client.RemoveObjectAsync(
                new RemoveObjectArgs().WithBucket(_bucket).WithObject(ObjectKey(sessionId)),
                ct);
        }
        catch (Exception ex) when (ex is ObjectNotFoundException or BucketNotFoundException)
        {
            // Deleting an already-gone object is a no-op.
        }
    }

    private async Task EnsureBucketAsync(CancellationToken ct)
    {
        var exists = await _client.BucketExistsAsync(new BucketExistsArgs().WithBucket(_bucket), ct);
        if (!exists)
        {
            await _client.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucket), ct);
        }
    }

    private static string ObjectKey(string sessionId) => $"receipts/{sessionId}";
}
