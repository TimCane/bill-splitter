namespace BillSplitter.Api.Auth;

/// <summary>Names shared across the auth handler, policies and controllers.</summary>
public static class ParticipantAuth
{
    public const string Scheme = "ParticipantToken";

    /// <summary>Requires a matched participant (any member of the session).</summary>
    public const string ParticipantPolicy = "Participant";

    /// <summary>Requires the matched participant to be the host.</summary>
    public const string HostPolicy = "HostOnly";

    public const string ParticipantIdClaim = "participantId";
    public const string SessionIdClaim = "sessionId";
    public const string IsHostClaim = "isHost";
}
