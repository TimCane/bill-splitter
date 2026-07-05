using System.Security.Claims;
using BillSplitter.Api.Auth;
using BillSplitter.Api.Configuration;
using BillSplitter.Api.Dtos;
using BillSplitter.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SessionOptions = BillSplitter.Api.Configuration.SessionOptions;

namespace BillSplitter.Api.Controllers;

[ApiController]
[Route("api/v1/sessions/{sessionId}/participants")]
public sealed class ParticipantsController(
    ISessionStore store,
    IIdGenerator ids,
    SnapshotMapper mapper,
    ISessionNotifier notifier,
    TimeProvider clock,
    IOptions<SessionOptions> sessionOptions)
    : ControllerBase
{
    private readonly SessionOptions _options = sessionOptions.Value;

    /// <summary>Join an open session. Anonymous; the returned token is the caller's
    /// credential thereafter (docs/04-api-contract.md#post-apiv1sessionssessionidparticipants).</summary>
    [HttpPost]
    public async Task<IActionResult> Join(string sessionId, [FromBody] JoinRequest request, CancellationToken ct)
    {
        var participantId = ids.NewId();
        var token = ids.NewToken();

        var record = await store.MutateAsync(
            sessionId,
            s => s.Join(participantId, TokenHasher.Hash(token), request.DisplayName, clock.GetUtcNow(), _options.MaxParticipants),
            ct);
        await notifier.SnapshotUpdatedAsync(sessionId, ct);

        var snapshot = mapper.Map(record.Session, record.Ttl);
        return StatusCode(StatusCodes.Status201Created, new JoinResponse(participantId, token, snapshot));
    }

    /// <summary>Set own display name. Any participant, in Review or Open.</summary>
    [HttpPut("me")]
    [Authorize(Policy = ParticipantAuth.ParticipantPolicy)]
    public async Task<IActionResult> RenameMe(string sessionId, [FromBody] RenameRequest request, CancellationToken ct)
    {
        var participantId = User.FindFirstValue(ParticipantAuth.ParticipantIdClaim)!;

        var record = await store.MutateAsync(
            sessionId,
            s => s.RenameParticipant(participantId, request.DisplayName),
            ct);
        await notifier.SnapshotUpdatedAsync(sessionId, ct);

        return Ok(mapper.Map(record.Session, record.Ttl));
    }
}
