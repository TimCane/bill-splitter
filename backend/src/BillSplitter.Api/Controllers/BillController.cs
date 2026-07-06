using BillSplitter.Api.Auth;
using BillSplitter.Api.Dtos;
using BillSplitter.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BillSplitter.Api.Controllers;

/// <summary>
/// Extras + printed total + currency edit - host only, state <c>Review</c> only
/// (docs/04-api-contract.md#put-apiv1sessionssessionidbill).
/// </summary>
[ApiController]
[Route("api/v1/sessions/{sessionId}/bill")]
[Authorize(Policy = ParticipantAuth.HostPolicy)]
public sealed class BillController(
    ISessionStore store,
    SnapshotMapper mapper,
    ISessionNotifier notifier)
    : ControllerBase
{
    [HttpPut]
    public async Task<IActionResult> Set(string sessionId, [FromBody] BillRequest request, CancellationToken ct)
    {
        var record = await store.MutateAsync(
            sessionId,
            s => s.SetBill(request.TaxMinor, request.TipMinor, request.ServiceMinor, request.TotalMinor, request.Currency),
            ct);
        await notifier.SnapshotUpdatedAsync(sessionId, ct);

        return Ok(mapper.Map(record.Session, record.Ttl));
    }
}
