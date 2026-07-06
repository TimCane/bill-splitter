namespace BillSplitter.Domain;

/// <summary>A session plus the remaining TTL of its Redis key, read together so
/// the snapshot's <c>expiresAt</c> reflects the key's real expiry
/// (docs/04-api-contract.md#sessionsnapshotdto).</summary>
public sealed record SessionRecord(Session Session, TimeSpan Ttl);

/// <summary>
/// The single write funnel for sessions. Implemented over Redis with an
/// optimistic compare-and-swap loop; callers never touch Redis semantics and
/// domain rules never know about Redis (docs/07-backend-design.md#the-mutate-pattern).
/// </summary>
public interface ISessionStore
{
    Task<SessionRecord?> GetAsync(string sessionId, CancellationToken ct);

    /// <summary>Store a freshly created session with the 24h TTL.</summary>
    Task<SessionRecord> CreateAsync(Session session, CancellationToken ct);

    /// <summary>Read, apply <paramref name="mutation"/> (which throws on rule
    /// violations before anything is written), bump the version and CAS-commit,
    /// retrying on version conflicts. Preserves the key's TTL (KEEPTTL).</summary>
    Task<SessionRecord> MutateAsync(string sessionId, Action<Session> mutation, CancellationToken ct);

    /// <summary>Resolve a typed-in short code to its session id, or null.</summary>
    Task<string?> ResolveCodeAsync(string shortCode, CancellationToken ct);
}
