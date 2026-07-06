# Redis schema

Redis is the only session store. Run it with persistence disabled
(`save ""`, `appendonly no`) - losing Redis loses live sessions and that is
acceptable by design ([ADR-0004](adr/0004-ephemeral-by-design.md)).

Client library: StackExchange.Redis (single multiplexer, DI singleton).

## Keys

| Key | Type | Value | TTL |
| --- | --- | --- | --- |
| `session:{sessionId}` | string | JSON document (below) | 24h from create; reset to 1h at finalize |
| `code:{shortCode}` | string | `sessionId` | matches its session's remaining TTL at mint |

That is the whole schema. No indexes, no scans, no cross-session queries
exist anywhere in the app.

## The session document

One JSON serialization of the `Session` aggregate
([02-domain-model.md](02-domain-model.md)), System.Text.Json, camelCase.
Example (trimmed):

```json
{
  "id": "u3K9mPd2QYqLxN7cWvB4Ag",
  "version": 17,
  "state": "Open",
  "currency": "GBP",
  "shortCode": "K7MPQ2",
  "createdAt": "2026-07-04T19:02:11Z",
  "finalizedAt": null,
  "hostParticipantId": "aB3xY9kQ2mN8pL4dF6hJ1w",
  "participants": [
    {
      "id": "aB3xY9kQ2mN8pL4dF6hJ1w",
      "tokenHash": "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
      "displayName": "Tim",
      "joinedAt": "2026-07-04T19:02:11Z"
    }
  ],
  "items": [
    {
      "id": "qW8eR2tY6uI9oP3aS5dF7g",
      "name": "Margherita",
      "quantity": 1,
      "priceMinor": 1250,
      "claims": [{ "participantId": "aB3xY9kQ2mN8pL4dF6hJ1w", "shares": 1 }]
    }
  ],
  "bill": { "taxMinor": 0, "tipMinor": 500, "serviceMinor": 0, "totalMinor": 5450 },
  "ocr": { "status": "Done", "failureReason": null, "warnings": [] }
}
```

Computed values (subtotal, per-participant totals, unclaimed total) are
**never stored** - the snapshot builder derives them on every read.

## Concurrency

Two people tapping the same item at once is the normal case. Mutations use
optimistic compare-and-swap on `version` via a Lua script
([ADR-0003](adr/0003-session-json-cas.md)):

```lua
-- KEYS[1] = session:{id}
-- KEYS[2] = code:{shortCode} (finalize only)
-- ARGV[1] = expected version (int)
-- ARGV[2] = new JSON document (version already incremented)
-- ARGV[3] = new TTL in seconds (finalize only; absent = keep TTL)
-- returns 1 on success, 0 on conflict, -1 on missing key
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
```

Write path in `RedisSessionStore`:

1. `GET session:{id}`, deserialize.
2. Apply the mutation to the aggregate (domain method), increment
   `Version`.
3. `EVALSHA` the CAS script with the pre-mutation version.
4. On `0` (conflict): re-read and retry from step 1, up to 5 attempts,
   with a tiny jittered delay (random 0-10ms, doubling per attempt) so
   lockstep writers interleave instead of colliding again.
   After 5 failures: `503` / hub error - should never happen in practice;
   if it does, it is a bug worth an alert.
5. On `-1`: session expired -> `404` / `session-not-found` hub error.

`KEEPTTL` preserves the TTL on every write; the TTL is only ever set
explicitly at create (24h) and inside the finalize CAS call (1h) - never
in a follow-up step a crash could skip.

## Lifecycle operations

| Operation | Redis effect |
| --- | --- |
| Create session | `SET session:{id} {json} EX 86400` |
| Any mutation | CAS script above (`KEEPTTL`) |
| Open | mint the code key (below), then CAS write (state -> `Open`, shortCode set) |
| Finalize | CAS write (state -> `Finalized`) passing TTL 3600 - the script shrinks the session and code keys atomically with the write |
| Expiry | Redis TTL. No cleanup jobs, no tombstones. |

Short-code minting: generate 6 chars from the short-code alphabet
([02-domain-model.md](02-domain-model.md#entities) owns it),
`SET code:{code} {id} NX EX {remaining-session-ttl}`; on collision (NX
fails) generate a new code, up to 5 attempts. The code key is minted
before the CAS commit stores `shortCode`, so a failed CAS or a crash never
leaves a session displaying a code it does not own - an orphaned code key
just expires with its TTL. ~1.1e9 combinations vs a handful of live
sessions: collisions are lottery-rare.

## Sizing

A worst-case session (20 participants, 100 items, every item claimed by 4
people) serializes to roughly 40KB. Typical sessions are 2-6KB. Rewriting
the whole document per mutation is deliberate and cheap
([ADR-0003](adr/0003-session-json-cas.md)).
