using System.Threading.Channels;

namespace BillSplitter.Infrastructure.Ocr;

/// <summary>A queued OCR job. Only the session id travels; the worker fetches the
/// image from MinIO so nothing large sits in memory (docs/06-ocr-service.md#backend-job-flow).</summary>
public sealed record OcrJob(string SessionId);

/// <summary>
/// The bounded, in-process work queue between the create endpoint (producer) and
/// <c>OcrWorker</c> (consumer). Capacity is <c>Ocr__QueueCapacity</c> (16);
/// <see cref="EnqueueAsync"/> returns <c>false</c> when full so the controller can
/// answer 429 rather than block (docs/07-backend-design.md#infrastructure-project).
/// In-process by design: a restart drops queued jobs and the lazy recovery heals
/// stuck sessions (docs/06-ocr-service.md#backend-job-flow).
/// </summary>
public sealed class OcrQueue
{
    private readonly Channel<OcrJob> _channel;

    public OcrQueue(int capacity)
    {
        _channel = Channel.CreateBounded<OcrJob>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    public ChannelReader<OcrJob> Reader => _channel.Reader;

    /// <summary>Enqueue a job, or return <c>false</c> if the queue is at capacity.
    /// Never blocks: a full queue is backpressure the caller turns into a 429.</summary>
    public ValueTask<bool> EnqueueAsync(OcrJob job, CancellationToken ct) =>
        ValueTask.FromResult(_channel.Writer.TryWrite(job));
}
