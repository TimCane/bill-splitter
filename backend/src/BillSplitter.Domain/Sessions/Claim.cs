using System.Text.Json.Serialization;

namespace BillSplitter.Domain.Sessions;

/// <summary>One participant's stake in a line item. One claim per participant
/// per item; <see cref="Shares"/> is an integer weight 1-99 (docs/02-domain-model.md).</summary>
public sealed class Claim
{
    [JsonConstructor]
    public Claim(string participantId, int shares)
    {
        ParticipantId = participantId;
        Shares = shares;
    }

    public string ParticipantId { get; private set; }

    public int Shares { get; private set; }

    internal void SetShares(int shares) => Shares = shares;
}
