using BillSplitter.Api.Dtos;
using BillSplitter.Domain.Abstractions;
using BillSplitter.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace BillSplitter.Api.Controllers;

/// <summary>
/// Resolves a typed-in short code to its session id. Anonymous and rate limited as a
/// brute-force guard - the code space is small enough to enumerate without one. The
/// resolve-code windows (10/min + 100/day) live in the central per-IP policy table
/// (docs/04-api-contract.md#get-apiv1codesshortcode,
/// docs/10-security-privacy.md#rate-limits, BillSplitter.Api.Configuration.RateLimiting).
/// </summary>
[ApiController]
[Route("api/v1/codes")]
public sealed class CodesController(ISessionStore store) : ControllerBase
{
    [HttpGet("{shortCode}")]
    public async Task<IActionResult> Resolve(string shortCode, CancellationToken ct)
    {
        var sessionId = await store.ResolveCodeAsync(shortCode, ct)
            ?? throw new DomainException(ErrorCodes.SessionNotFound, shortCode);

        return Ok(new ResolveCodeResponse(sessionId));
    }
}
