using BillSplitter.Api.Configuration;
using BillSplitter.Api.Dtos;
using BillSplitter.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BillSplitter.Api.Controllers;

/// <summary>
/// Resolves a typed-in short code to its session id. Anonymous and rate limited as a
/// brute-force guard - the code space is small enough to enumerate without one
/// (docs/04-api-contract.md#get-apiv1codesshortcode,
/// docs/10-security-privacy.md#rate-limits).
/// </summary>
[ApiController]
[Route("api/v1/codes")]
public sealed class CodesController(ISessionStore store) : ControllerBase
{
    [HttpGet("{shortCode}")]
    [EnableRateLimiting(RateLimitPolicies.CodeResolve)]
    public async Task<IActionResult> Resolve(string shortCode, CancellationToken ct)
    {
        var sessionId = await store.ResolveCodeAsync(shortCode, ct)
            ?? throw new DomainException(ErrorCodes.SessionNotFound, shortCode);

        return Ok(new ResolveCodeResponse(sessionId));
    }
}
