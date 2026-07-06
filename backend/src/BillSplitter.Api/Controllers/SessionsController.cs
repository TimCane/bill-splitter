using BillSplitter.Api.Auth;
using BillSplitter.Api.Configuration;
using BillSplitter.Api.Dtos;
using BillSplitter.Api.Http;
using BillSplitter.Api.Ocr;
using BillSplitter.Domain;
using BillSplitter.Infrastructure.Ocr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SessionOptions = BillSplitter.Api.Configuration.SessionOptions;

namespace BillSplitter.Api.Controllers;

[ApiController]
[Route("api/v1/sessions")]
public sealed class SessionsController(
    ISessionStore store,
    IReceiptStorage storage,
    IIdGenerator ids,
    SnapshotMapper mapper,
    OcrQueue queue,
    StaleOcrRecovery recovery,
    TimeProvider clock,
    IOptions<SessionOptions> sessionOptions)
    : ControllerBase
{
    private readonly SessionOptions _options = sessionOptions.Value;

    /// <summary>Create a session from a receipt photo, store the image and queue
    /// the OCR job. The session stays in <c>Processing</c> until the worker lands
    /// it in <c>Review</c> (docs/06-ocr-service.md#backend-job-flow).</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromForm] IFormFile? image, CancellationToken ct)
    {
        if (image is null || image.Length == 0)
        {
            throw new DomainException(ErrorCodes.Validation, "image is required");
        }

        if (image.Length > _options.MaxUploadBytes)
        {
            throw new DomainException(ErrorCodes.ImageTooLarge);
        }

        using var buffer = new MemoryStream();
        await image.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();

        var contentType = ImageSniffer.Detect(bytes)
            ?? throw new DomainException(ErrorCodes.Validation, "image must be JPEG or PNG");

        var sessionId = ids.NewId();
        var hostId = ids.NewId();
        var token = ids.NewToken();
        var session = Session.Create(sessionId, hostId, TokenHasher.Hash(token), clock.GetUtcNow());

        await store.CreateAsync(session, ct);
        await storage.PutAsync(sessionId, bytes, contentType, ct);

        // Queue OCR; the worker broadcasts progress. A full queue is backpressure.
        if (!await queue.EnqueueAsync(new OcrJob(sessionId), ct))
        {
            throw new DomainException(ErrorCodes.RateLimited, "the OCR queue is full");
        }

        return Accepted(new CreateSessionResponse(sessionId, hostId, token, "Host"));
    }

    [HttpGet("{sessionId}")]
    public async Task<IActionResult> Get(string sessionId, CancellationToken ct)
    {
        var record = await store.GetAsync(sessionId, ct)
            ?? throw new DomainException(ErrorCodes.SessionNotFound, sessionId);

        record = await recovery.RecoverIfStaleAsync(record, ct);

        return Ok(mapper.Map(record.Session, record.Ttl));
    }

    /// <summary>Stream the stored receipt image with its stored content type. Host
    /// only; the object lives from create until open, so this 404s once the split is
    /// opened (docs/04-api-contract.md#get-apiv1sessionssessionidreceipt).</summary>
    [HttpGet("{sessionId}/receipt")]
    [Authorize(Policy = ParticipantAuth.HostPolicy)]
    public async Task<IActionResult> Receipt(string sessionId, CancellationToken ct)
    {
        var receipt = await storage.GetAsync(sessionId, ct)
            ?? throw new DomainException(ErrorCodes.ReceiptNotFound, sessionId);

        return File(receipt.Content, receipt.ContentType);
    }
}
