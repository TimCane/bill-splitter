using BillSplitter.Domain;
using StackExchange.Redis;

namespace BillSplitter.Infrastructure.Redis;

/// <summary>
/// Redis-backed <see cref="ISessionStore"/>. Every write goes through the Lua CAS
/// funnel (docs/03-redis-schema.md#concurrency): read, apply the mutation, bump the
/// version, EVALSHA the compare-and-swap, retry on conflict. TTL is only ever set
/// at create (24h) and, later, inside the finalize CAS - never in a follow-up step
/// a crash could skip, so writes use KEEPTTL.
/// </summary>
public sealed class RedisSessionStore : ISessionStore
{
    private const int MaxAttempts = 5;

    // KEYS[1] = session:{id}, KEYS[2] = code:{shortCode} (finalize only).
    // ARGV[1] = expected version, ARGV[2] = new JSON (version already bumped),
    // ARGV[3] = new TTL seconds (finalize only; absent => KEEPTTL).
    // returns 1 success, 0 conflict, -1 missing key.
    private const string CasScript =
        """
        local raw = redis.call('GET', KEYS[1])
        if not raw then return -1 end
        local current = cjson.decode(raw)
        if current.version ~= tonumber(ARGV[1]) then return 0 end
        if ARGV[3] then
          redis.call('SET', KEYS[1], ARGV[2], 'EX', ARGV[3])
          if KEYS[2] then redis.call('EXPIRE', KEYS[2], ARGV[3]) end
        else
          redis.call('SET', KEYS[1], ARGV[2], 'KEEPTTL')
        end
        return 1
        """;

    private readonly IDatabase _db;
    private readonly TimeSpan _sessionTtl;

    public RedisSessionStore(IConnectionMultiplexer redis, TimeSpan sessionTtl)
    {
        _db = redis.GetDatabase();
        _sessionTtl = sessionTtl;
    }

    public async Task<SessionRecord?> GetAsync(string sessionId, CancellationToken ct)
    {
        var key = SessionKey(sessionId);
        var rawTask = _db.StringGetAsync(key);
        var ttlTask = _db.KeyTimeToLiveAsync(key);
        await Task.WhenAll(rawTask, ttlTask);

        var raw = rawTask.Result;
        if (raw.IsNull)
        {
            return null;
        }

        return new SessionRecord(SessionSerialization.Deserialize(raw!), ttlTask.Result ?? _sessionTtl);
    }

    public async Task<SessionRecord> CreateAsync(Session session, CancellationToken ct)
    {
        var json = SessionSerialization.Serialize(session);
        await _db.StringSetAsync(SessionKey(session.Id), json, _sessionTtl);
        return new SessionRecord(session, _sessionTtl);
    }

    public async Task<SessionRecord> MutateAsync(string sessionId, Action<Session> mutation, CancellationToken ct)
    {
        var key = SessionKey(sessionId);

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var rawTask = _db.StringGetAsync(key);
            var ttlTask = _db.KeyTimeToLiveAsync(key);
            await Task.WhenAll(rawTask, ttlTask);

            var raw = rawTask.Result;
            if (raw.IsNull)
            {
                throw new DomainException(ErrorCodes.SessionNotFound, sessionId);
            }

            var session = SessionSerialization.Deserialize(raw!);
            var expectedVersion = session.Version;

            // Domain rules throw here, before any write is attempted.
            mutation(session);
            session.IncrementVersion();

            var newJson = SessionSerialization.Serialize(session);
            var outcome = (long)await _db.ScriptEvaluateAsync(
                CasScript,
                [key],
                [expectedVersion, newJson]);

            switch (outcome)
            {
                case 1:
                    return new SessionRecord(session, ttlTask.Result ?? _sessionTtl);
                case -1:
                    throw new DomainException(ErrorCodes.SessionNotFound, sessionId);
                default:
                    await BackoffAsync(attempt, ct);
                    break;
            }
        }

        // Should never happen in practice - a bug worth an alert if it does.
        throw new DomainException(ErrorCodes.ConflictRetryExhausted, sessionId);
    }

    public async Task<string?> ResolveCodeAsync(string shortCode, CancellationToken ct)
    {
        var value = await _db.StringGetAsync(CodeKey(shortCode));
        return value.IsNull ? null : value.ToString();
    }

    // Tiny jittered delay (0-10ms base, doubling per attempt) so lockstep writers
    // interleave instead of colliding again.
    private static Task BackoffAsync(int attempt, CancellationToken ct)
    {
        var baseMs = Random.Shared.Next(0, 11);
        var delayMs = baseMs << attempt;
        return delayMs == 0 ? Task.CompletedTask : Task.Delay(delayMs, ct);
    }

    private static RedisKey SessionKey(string sessionId) => $"session:{sessionId}";

    private static RedisKey CodeKey(string shortCode) => $"code:{shortCode}";
}
