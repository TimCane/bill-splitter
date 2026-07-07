using BillSplitter.Domain.Abstractions;
using BillSplitter.Domain.Common;
using BillSplitter.Domain.Sessions;
using Microsoft.Extensions.Logging;
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
    private readonly IIdGenerator _ids;
    private readonly TimeSpan _sessionTtl;
    private readonly TimeSpan _finalizedTtl;
    private readonly ILogger<RedisSessionStore> _logger;

    public RedisSessionStore(
        IConnectionMultiplexer redis,
        IIdGenerator ids,
        TimeSpan sessionTtl,
        TimeSpan finalizedTtl,
        ILogger<RedisSessionStore> logger)
    {
        _db = redis.GetDatabase();
        _ids = ids;
        _sessionTtl = sessionTtl;
        _finalizedTtl = finalizedTtl;
        _logger = logger;
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

    public Task<SessionRecord> MutateAsync(string sessionId, Action<Session> mutation, CancellationToken ct) =>
        MutateAsync(sessionId, mutation, newTtl: null, ct);

    public Task<SessionRecord> FinalizeAsync(string sessionId, Action<Session> mutation, CancellationToken ct) =>
        MutateAsync(sessionId, mutation, newTtl: _finalizedTtl, ct);

    // The one CAS write funnel. newTtl null preserves the key's TTL (KEEPTTL); a
    // value shrinks the session and its code key together inside the same commit,
    // the path finalize takes (docs/03-redis-schema.md#lifecycle-operations).
    private async Task<SessionRecord> MutateAsync(
        string sessionId, Action<Session> mutation, TimeSpan? newTtl, CancellationToken ct)
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
            RedisKey[] keys = [key];
            RedisValue[] argv = [expectedVersion, newJson];
            if (newTtl is { } ttl)
            {
                // The code key rides along so it expires with the session; a session
                // finalized before it was opened has none, so shrink the session key alone.
                keys = session.ShortCode is { } code ? [key, CodeKey(code)] : [key];
                argv = [expectedVersion, newJson, (long)ttl.TotalSeconds];
            }

            var outcome = (long)await _db.ScriptEvaluateAsync(CasScript, keys, argv);

            switch (outcome)
            {
                case 1:
                    return new SessionRecord(session, newTtl ?? ttlTask.Result ?? _sessionTtl);
                case -1:
                    throw new DomainException(ErrorCodes.SessionNotFound, sessionId);
                default:
                    // Expected under contention; the count is what tests and
                    // alerting watch, so keep the message stable.
                    _logger.LogDebug("CAS conflict on session write, attempt {Attempt}", attempt + 1);
                    await BackoffAsync(attempt, ct);
                    break;
            }
        }

        // Should never happen in practice - a bug worth an alert if it does.
        throw new DomainException(ErrorCodes.ConflictRetryExhausted, sessionId);
    }

    public async Task<SessionRecord> OpenAsync(string sessionId, string actingParticipantId, CancellationToken ct)
    {
        // Read once for the remaining TTL the code key must inherit. The mutation
        // below re-reads under CAS, so this value only needs to be the mint TTL.
        var current = await GetAsync(sessionId, ct)
            ?? throw new DomainException(ErrorCodes.SessionNotFound, sessionId);

        var code = await MintCodeAsync(sessionId, current.Ttl, ct);

        try
        {
            return await MutateAsync(sessionId, s => s.Open(actingParticipantId, code), ct);
        }
        catch
        {
            // The code is minted before the transition guards run, so a rejected
            // Open (wrong state, non-host, CAS conflict) would leave an orphan key
            // that still resolves to the session. Drop it so only the code the
            // host was shown ever resolves (docs/03-redis-schema.md#lifecycle-operations).
            await _db.KeyDeleteAsync(CodeKey(code));
            throw;
        }
    }

    public async Task<string?> ResolveCodeAsync(string shortCode, CancellationToken ct)
    {
        var value = await _db.StringGetAsync(CodeKey(shortCode));
        return value.IsNull ? null : value.ToString();
    }

    // SET code:{code} {sessionId} NX EX {remaining-ttl}: claim a fresh code, retrying
    // on the lottery-rare collision (docs/03-redis-schema.md#lifecycle-operations).
    private async Task<string> MintCodeAsync(string sessionId, TimeSpan ttl, CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var code = _ids.NewShortCode();
            if (await _db.StringSetAsync(CodeKey(code), sessionId, ttl, When.NotExists))
            {
                return code;
            }
        }

        throw new DomainException(ErrorCodes.ConflictRetryExhausted, sessionId);
    }

    // Tiny jittered delay (1-10ms base, doubling per attempt) so lockstep writers
    // interleave instead of colliding again. The base is never zero: a 0ms delay
    // would let contending writers retry in lockstep and burn the 5-attempt budget.
    private static Task BackoffAsync(int attempt, CancellationToken ct)
    {
        var baseMs = Random.Shared.Next(1, 11);
        var delayMs = baseMs << attempt;
        return Task.Delay(delayMs, ct);
    }

    private static RedisKey SessionKey(string sessionId) => $"session:{sessionId}";

    private static RedisKey CodeKey(string shortCode) => $"code:{shortCode}";
}
