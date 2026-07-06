using System.Security.Claims;
using BillSplitter.Api.Auth;
using BillSplitter.Api.Configuration;
using BillSplitter.Api.Dtos;
using BillSplitter.Api.Http;
using BillSplitter.Api.Ocr;
using BillSplitter.Domain;
using BillSplitter.Infrastructure.Ocr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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
    ISessionNotifier notifier,
    IEmailSender emailSender,
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

        // Decode-bomb guard: a valid header can still declare a ruinous canvas.
        // Reject on the header dimensions before the bytes reach OCR, which runs
        // the same check (docs/10-security-privacy.md#upload-hardening).
        if (!ImageDimensions.TryRead(bytes, out var width, out var height))
        {
            throw new DomainException(ErrorCodes.Validation, "image header is malformed");
        }

        if (width > _options.MaxImageDimension || height > _options.MaxImageDimension)
        {
            throw new DomainException(
                ErrorCodes.ImageTooLarge, $"image exceeds {_options.MaxImageDimension}px on an axis");
        }

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

    /// <summary>Open the split: mint the short code, commit the transition, then
    /// delete the receipt image. Returns the code and the join URL the host shares
    /// (docs/04-api-contract.md#post-apiv1sessionssessionidopen).</summary>
    [HttpPost("{sessionId}/open")]
    [Authorize(Policy = ParticipantAuth.HostPolicy)]
    public async Task<IActionResult> Open(string sessionId, CancellationToken ct)
    {
        var participantId = User.FindFirstValue(ParticipantAuth.ParticipantIdClaim)!;

        var record = await store.OpenAsync(sessionId, participantId, ct);

        // The image only exists to drive Review; once Open, it is gone for good.
        // Best-effort: the transition already committed, so a delete hiccup must
        // not strand the host on Review - the bucket lifecycle expires the object
        // within a day either way (MinioReceiptStorage).
        try
        {
            await storage.DeleteAsync(sessionId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Swallow: the lifecycle rule is the backstop.
        }

        await notifier.SnapshotUpdatedAsync(sessionId, ct);

        var snapshot = mapper.Map(record.Session, record.Ttl);
        return Ok(new OpenResponse(snapshot.ShortCode!, snapshot.JoinUrl!));
    }

    /// <summary>Finalize the split: lock claims, split the unclaimed items equally,
    /// shrink the session and code keys to ~1h and broadcast <c>SessionFinalized</c>.
    /// Host, <c>Open</c> only; returns the finalized snapshot
    /// (docs/04-api-contract.md#post-apiv1sessionssessionidfinalize).</summary>
    [HttpPost("{sessionId}/finalize")]
    [Authorize(Policy = ParticipantAuth.HostPolicy)]
    public async Task<IActionResult> Finalize(
        string sessionId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] FinalizeRequest? request,
        CancellationToken ct)
    {
        var participantId = User.FindFirstValue(ParticipantAuth.ParticipantIdClaim)!;

        var record = await store.FinalizeAsync(
            sessionId, s => s.Finalize(participantId, clock.GetUtcNow()), ct);

        var snapshot = mapper.Map(record.Session, record.Ttl);
        await notifier.SessionFinalizedAsync(sessionId, ct);

        if (!string.IsNullOrWhiteSpace(request?.Email))
        {
            // Fire and forget: the send outlives the request and never faults it.
            // The address is used only for this call - it reaches neither Redis nor
            // the logs (docs/04-api-contract.md, docs/10-security-privacy.md).
            _ = emailSender.SendSummaryAsync(request.Email, BuildSummary(snapshot), CancellationToken.None);
        }

        return Ok(snapshot);
    }

    // The email reconciles against its own rows: its total is the sum of the
    // per-person finalized totals, which the finalize invariant fixes to the whole
    // bill (docs/02-domain-model.md#invariants-property-test-these).
    private static SummaryEmail BuildSummary(SessionSnapshotDto snapshot)
    {
        var nameById = snapshot.Participants.ToDictionary(p => p.ParticipantId, p => p.DisplayName);
        var lines = snapshot.Totals
            .Select(t => new SummaryEmailLine(nameById.GetValueOrDefault(t.ParticipantId, "?"), t.TotalMinor))
            .ToList();

        return new SummaryEmail(
            snapshot.Currency, lines.Sum(l => l.TotalMinor), snapshot.UnclaimedTotalMinor, lines);
    }
}
