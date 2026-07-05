using System.Text.Json.Serialization;

namespace BillSplitter.Domain;

/// <summary>A receipt line. <see cref="PriceMinor"/> is the whole line total, not
/// a unit price; <see cref="Quantity"/> is informational only and never affects
/// math (docs/02-domain-model.md#lineitem).</summary>
public sealed class LineItem
{
    private readonly List<Claim> _claims;

    [JsonConstructor]
    public LineItem(string id, string name, int quantity, long priceMinor, IReadOnlyList<Claim>? claims)
    {
        Id = id;
        Name = name;
        Quantity = quantity;
        PriceMinor = priceMinor;
        _claims = claims is null ? [] : [.. claims];
    }

    public string Id { get; private set; }

    public string Name { get; private set; }

    public int Quantity { get; private set; }

    public long PriceMinor { get; private set; }

    public IReadOnlyList<Claim> Claims => _claims;

    internal void Update(string name, int quantity, long priceMinor)
    {
        Name = name;
        Quantity = quantity;
        PriceMinor = priceMinor;
    }

    /// <summary>Upsert a participant's claim at the given weight.</summary>
    internal void SetShares(string participantId, int shares)
    {
        var existing = _claims.Find(c => c.ParticipantId == participantId);
        if (existing is null)
        {
            _claims.Add(new Claim(participantId, shares));
        }
        else
        {
            existing.SetShares(shares);
        }
    }

    /// <summary>Remove a participant's claim; no-op if they have none.</summary>
    internal void Unclaim(string participantId) =>
        _claims.RemoveAll(c => c.ParticipantId == participantId);
}
