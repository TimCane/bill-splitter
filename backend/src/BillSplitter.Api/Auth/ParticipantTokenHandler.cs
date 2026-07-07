using System.Security.Claims;
using System.Text.Encodings.Web;
using BillSplitter.Api.Http;
using BillSplitter.Domain.Abstractions;
using BillSplitter.Domain.Common;
using BillSplitter.Domain.Sessions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Claim = System.Security.Claims.Claim;

namespace BillSplitter.Api.Auth;

/// <summary>
/// Resolves <c>{sessionId} + bearer token -> participant</c> and exposes it as
/// claims (docs/04-api-contract.md#auth). The token is taken from the
/// Authorization header or, for the hub, the <c>access_token</c> query slot
/// (docs/05-realtime-contract.md#connecting). A token that matches no participant
/// still authenticates but without a participant claim, so the authorization
/// policies reject it as <c>unknown-participant</c> (403) rather than
/// <c>missing-token</c> (401).
/// </summary>
public sealed class ParticipantTokenHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly ISessionStore _store;

    public ParticipantTokenHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISessionStore store)
        : base(options, logger, encoder)
    {
        _store = store;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = ExtractToken();
        if (token is null)
        {
            return AuthenticateResult.NoResult();
        }

        var sessionId = ExtractSessionId();
        if (sessionId is null)
        {
            return AuthenticateResult.NoResult();
        }

        var identity = new ClaimsIdentity(ParticipantAuth.Scheme);
        var record = await _store.GetAsync(sessionId, Context.RequestAborted);
        var participant = record?.Session.FindByTokenHash(TokenHasher.Hash(token));
        if (participant is not null)
        {
            var isHost = record!.Session.IsHost(participant.Id);
            identity.AddClaim(new Claim(ParticipantAuth.ParticipantIdClaim, participant.Id));
            identity.AddClaim(new Claim(ParticipantAuth.IsHostClaim, isHost ? "true" : "false"));
        }

        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, ParticipantAuth.Scheme));
    }

    // No token at all -> 401 missing-token.
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        return ApiProblem.WriteAsync(Context, ErrorCodes.MissingToken);
    }

    // Authenticated but the policy failed: a matched participant means the host
    // gate failed (not-host); an unmatched token means unknown-participant.
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        var code = Context.User.HasClaim(c => c.Type == ParticipantAuth.ParticipantIdClaim)
            ? ErrorCodes.NotHost
            : ErrorCodes.UnknownParticipant;
        return ApiProblem.WriteAsync(Context, code);
    }

    private string? ExtractToken()
    {
        var header = Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var value = header["Bearer ".Length..].Trim();
            return value.Length == 0 ? null : value;
        }

        // The query-string slot is only for the hub (WebSockets cannot send an
        // Authorization header - docs/05-realtime-contract.md#connecting). Accepting
        // it on REST routes would leak the participant token into access logs.
        if (Request.Path.StartsWithSegments("/hubs"))
        {
            var queryToken = Request.Query["access_token"].ToString();
            return string.IsNullOrEmpty(queryToken) ? null : queryToken;
        }

        return null;
    }

    private string? ExtractSessionId()
    {
        if (Request.RouteValues.TryGetValue("sessionId", out var routeValue) && routeValue is string routeId)
        {
            return routeId;
        }

        var queryId = Request.Query["sessionId"].ToString();
        return string.IsNullOrEmpty(queryId) ? null : queryId;
    }
}
