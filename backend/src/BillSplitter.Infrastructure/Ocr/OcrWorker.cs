using BillSplitter.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BillSplitter.Infrastructure.Ocr;

/// <summary>
/// Consumes <see cref="OcrQueue"/> at most <c>Ocr__MaxConcurrency</c> (2) jobs at
/// a time and drives each session from <c>Processing</c> to <c>Review</c>
/// (docs/06-ocr-service.md#backend-job-flow). Every write is a CAS conditional on
/// the session still being <c>Processing</c>, so a slow-but-live worker and the
/// lazy stale recovery can never both land: whoever CAS-writes first wins and the
/// other is a silent no-op.
/// </summary>
public sealed class OcrWorker(
    OcrQueue queue,
    IServiceScopeFactory scopeFactory,
    int maxConcurrency,
    ILogger<OcrWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Parallel.ForEachAsync(
                queue.Reader.ReadAllAsync(stoppingToken),
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxConcurrency,
                    CancellationToken = stoppingToken,
                },
                async (job, ct) => await ProcessAsync(job, ct));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown: queued and in-flight jobs are dropped by design.
        }
    }

    private async Task ProcessAsync(OcrJob job, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var store = services.GetRequiredService<ISessionStore>();
        var notifier = services.GetRequiredService<ISessionNotifier>();

        try
        {
            // 1. Claim the job: Processing only while still Processing.
            var claimed = await store.MutateAsync(job.SessionId, s => s.MarkOcrProcessing(), ct);
            if (claimed.Session.Ocr.Status != OcrStatus.Processing)
            {
                // Already recovered or abandoned - nothing to do.
                return;
            }

            await BroadcastAsync(notifier, claimed, ct);

            // 2. Fetch the image and call the sidecar.
            var storage = services.GetRequiredService<IReceiptStorage>();
            var client = services.GetRequiredService<IOcrClient>();
            var receipt = await storage.GetAsync(job.SessionId, ct)
                ?? throw new InvalidOperationException("receipt image is missing");
            var result = await RecognizeWithRetryAsync(client, receipt, ct);

            // 3. Parse into items + bill.
            var ids = services.GetRequiredService<IIdGenerator>();
            var parsed = ReceiptParser.Parse(result);
            var items = parsed.Items
                .Select(i => new LineItem(ids.NewId(), i.Name, i.Quantity, i.PriceMinor, null))
                .ToList();

            // 4. Apply the parse: Review/Done only while still Processing.
            var done = await store.MutateAsync(
                job.SessionId, s => s.CompleteOcr(items, parsed.Bill, parsed.Currency, parsed.Warnings), ct);
            await BroadcastAsync(notifier, done, ct);
        }
        // A sidecar timeout throws TaskCanceledException (an OperationCanceledException)
        // whose token is not ct, so filter on ct instead of the type: only a real
        // shutdown (ct cancelled) is left to unwind; every other failure - timeout
        // included - lands the session in Review/Failed.
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            await FailAsync(store, notifier, job.SessionId, ReasonFor(ex), ct);
        }
    }

    // Timeouts and HTTP error responses fail identically on a retry; only a
    // connection-level failure (refused, reset, DNS) is worth one more attempt
    // (docs/06-ocr-service.md#backend-job-flow).
    private static async Task<OcrResult> RecognizeWithRetryAsync(
        IOcrClient client, ReceiptObject receipt, CancellationToken ct)
    {
        try
        {
            return await RecognizeAsync(client, receipt, ct);
        }
        catch (HttpRequestException ex) when (IsConnectionLevel(ex))
        {
            return await RecognizeAsync(client, receipt, ct);
        }
    }

    private static async Task<OcrResult> RecognizeAsync(
        IOcrClient client, ReceiptObject receipt, CancellationToken ct)
    {
        // A fresh stream per attempt: StreamContent disposes the one it sends.
        using var image = new MemoryStream(receipt.Content, writable: false);
        return await client.RecognizeAsync(image, receipt.ContentType, ct);
    }

    private static bool IsConnectionLevel(HttpRequestException ex) =>
        ex.HttpRequestError is HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError;

    private async Task FailAsync(
        ISessionStore store, ISessionNotifier notifier, string sessionId, string reason, CancellationToken ct)
    {
        try
        {
            var failed = await store.MutateAsync(sessionId, s => s.FailOcr(reason), ct);
            await BroadcastAsync(notifier, failed, ct);
        }
        catch (DomainException ex) when (ex.Code == ErrorCodes.SessionNotFound)
        {
            // The session expired mid-job; nothing left to fail.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OCR failure handling did not complete for a session");
        }
    }

    private static string ReasonFor(Exception ex) => ex switch
    {
        TaskCanceledException => "OCR timed out",
        HttpRequestException => "OCR service was unavailable",
        _ => "OCR could not read the receipt",
    };

    private static async Task BroadcastAsync(ISessionNotifier notifier, SessionRecord record, CancellationToken ct)
    {
        var ocr = record.Session.Ocr;
        await notifier.OcrStatusChangedAsync(record.Session.Id, ocr.Status, ocr.FailureReason, ct);
        await notifier.SnapshotUpdatedAsync(record.Session.Id, ct);
    }
}
