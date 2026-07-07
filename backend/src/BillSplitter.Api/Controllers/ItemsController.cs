using BillSplitter.Api.Auth;
using BillSplitter.Api.Configuration;
using BillSplitter.Api.Dtos;
using BillSplitter.Domain.Abstractions;
using BillSplitter.Domain.Sessions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SessionOptions = BillSplitter.Api.Configuration.SessionOptions;

namespace BillSplitter.Api.Controllers;

/// <summary>
/// Item CRUD - host only, state <c>Review</c> only. Each mutation returns the
/// snapshot and broadcasts <c>SnapshotUpdated</c> so REST callers and hub listeners
/// converge (docs/04-api-contract.md#item-crud---host-only-state-review-only).
/// </summary>
[ApiController]
[Route("api/v1/sessions/{sessionId}/items")]
[Authorize(Policy = ParticipantAuth.HostPolicy)]
public sealed class ItemsController(
    ISessionStore store,
    IIdGenerator ids,
    SnapshotMapper mapper,
    ISessionNotifier notifier,
    IOptions<SessionOptions> sessionOptions)
    : ControllerBase
{
    private readonly SessionOptions _options = sessionOptions.Value;

    [HttpPost]
    public async Task<IActionResult> Add(string sessionId, [FromBody] ItemRequest request, CancellationToken ct)
    {
        var itemId = ids.NewId();

        var record = await store.MutateAsync(
            sessionId,
            s => s.AddItem(itemId, request.Name, request.Quantity, request.PriceMinor, _options.MaxItems),
            ct);
        await notifier.SnapshotUpdatedAsync(sessionId, ct);

        return StatusCode(StatusCodes.Status201Created, mapper.Map(record.Session, record.Ttl));
    }

    [HttpPut("{itemId}")]
    public async Task<IActionResult> Update(
        string sessionId, string itemId, [FromBody] ItemRequest request, CancellationToken ct)
    {
        var record = await store.MutateAsync(
            sessionId,
            s => s.UpdateItem(itemId, request.Name, request.Quantity, request.PriceMinor),
            ct);
        await notifier.SnapshotUpdatedAsync(sessionId, ct);

        return Ok(mapper.Map(record.Session, record.Ttl));
    }

    [HttpDelete("{itemId}")]
    public async Task<IActionResult> Remove(string sessionId, string itemId, CancellationToken ct)
    {
        var record = await store.MutateAsync(sessionId, s => s.RemoveItem(itemId), ct);
        await notifier.SnapshotUpdatedAsync(sessionId, ct);

        return Ok(mapper.Map(record.Session, record.Ttl));
    }
}
