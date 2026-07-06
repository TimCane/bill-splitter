using BillSplitter.Api.Configuration;
using BillSplitter.Api.Dtos;
using BillSplitter.Api.Http;
using BillSplitter.Domain;
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
    ISessionNotifier notifier,
    TimeProvider clock,
    IOptions<SessionOptions> sessionOptions)
    : ControllerBase
{
    private readonly SessionOptions _options = sessionOptions.Value;

    /// <summary>Create a session from a receipt photo. OCR is faked as
    /// instant-empty-Review in M2 (docs/14-build-order.md#m2---session-core).</summary>
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

        // Fake OCR: land straight in Review with no items (real pipeline is M3).
        await store.MutateAsync(
            sessionId,
            s => s.CompleteOcr([], new Bill(0, 0, 0, 0), Session.DefaultCurrency),
            ct);
        await notifier.SnapshotUpdatedAsync(sessionId, ct);

        return Accepted(new CreateSessionResponse(sessionId, hostId, token, "Host"));
    }

    [HttpGet("{sessionId}")]
    public async Task<IActionResult> Get(string sessionId, CancellationToken ct)
    {
        var record = await store.GetAsync(sessionId, ct)
            ?? throw new DomainException(ErrorCodes.SessionNotFound, sessionId);

        return Ok(mapper.Map(record.Session, record.Ttl));
    }
}
