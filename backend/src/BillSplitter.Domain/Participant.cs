using System.Text.Json.Serialization;

namespace BillSplitter.Domain;

/// <summary>A person in the session. Host-ness is not stored here -
/// <see cref="Session.HostParticipantId"/> is the single source
/// (docs/02-domain-model.md#participant).</summary>
public sealed class Participant
{
    [JsonConstructor]
    public Participant(string id, string tokenHash, string displayName, DateTimeOffset joinedAt)
    {
        Id = id;
        TokenHash = tokenHash;
        DisplayName = displayName;
        JoinedAt = joinedAt;
    }

    public string Id { get; private set; }

    /// <summary>Hex SHA-256 of the participant token. The raw token is returned
    /// once at create/join and never stored; this hash never leaves the domain.</summary>
    public string TokenHash { get; private set; }

    public string DisplayName { get; private set; }

    /// <summary>UTC; the stable ordering key for all split math.</summary>
    public DateTimeOffset JoinedAt { get; private set; }

    internal void Rename(string displayName) => DisplayName = displayName;
}
