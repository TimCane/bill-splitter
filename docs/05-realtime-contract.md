# Realtime contract (SignalR)

One hub: `SessionHub` at `/hubs/session`. WebSockets with SignalR's
automatic negotiation/fallback. The hub carries the high-frequency claim
gestures upstream and all live updates downstream
([ADR-0002](adr/0002-hub-claims-rest-lifecycle.md)).

## Connecting

```
/hubs/session?sessionId={sessionId}&access_token={participantToken}
```

- `access_token` is the standard SignalR query-string auth slot (WebSockets
  cannot send an Authorization header). On the long-polling fallback the JS
  client sends the token as an `Authorization: Bearer` header instead; the
  hub accepts both.
- `OnConnectedAsync`: resolve the session, hash the token, match a
  participant. No match or no session -> the connection is rejected
  (`HubException`, code `unauthorized`).
- Matched connections join SignalR group `session:{sessionId}` and
  immediately receive a `SnapshotUpdated` (heals any gap between REST
  rehydrate and connect).
- Reconnection: client uses `withAutomaticReconnect`; every reconnect
  replays the same handshake, so the post-connect snapshot makes catch-up
  logic unnecessary.

Connection state is never business state: a participant who disconnects
stays in the session with their claims intact.

## Client -> server methods

All methods: allowed only in state `Open`, identified by the connection's
participant, return `Task` (errors via `HubException`). Each successful
mutation triggers a group-wide `SnapshotUpdated`.

| Method | Args | Semantics |
| --- | --- | --- |
| `ClaimItem` | `(string itemId)` | upsert my claim with `shares = 1`; no-op if already claimed by me with 1 share |
| `UnclaimItem` | `(string itemId)` | remove my claim; no-op if not claimed by me |
| `SetShares` | `(string itemId, int shares)` | `shares` 1-99: upsert my claim at that weight. Claims are removed only via `UnclaimItem`; out-of-range `shares` is an error |

`ClaimItem` is sugar for `SetShares(itemId, 1)` - kept as a separate method
so the common gesture is a single argument.

### Hub errors

`HubException` message is a stable code, mirroring the REST error table:

| Code | When |
| --- | --- |
| `unauthorized` | bad token at connect |
| `session-not-found` | session expired mid-connection |
| `wrong-state` | claim gesture outside `Open` |
| `item-not-found` | stale item id |
| `validation` | `shares` outside 1-99 |
| `conflict-retry-exhausted` | CAS gave up (see [03-redis-schema.md](03-redis-schema.md#concurrency)) |

The client surfaces `wrong-state` after finalize as a soft "session was
finalized" refresh, not an error toast
([09-ux-flows.md](09-ux-flows.md)).

## Server -> client events

| Event | Payload | When |
| --- | --- | --- |
| `SnapshotUpdated` | `SessionSnapshotDto` | on connect; after every successful mutation (hub, REST, or the OCR worker) |
| `OcrStatusChanged` | `{ status, failureReason }` | OCR job transitions (`Processing`, `Done`, `Failed`) - only the host is connected during these states, but it is broadcast to the group regardless. A hint only: each transition is paired with a `SnapshotUpdated` carrying the authoritative state, so clients render from snapshots and a stale hint is harmless |
| `SessionFinalized` | `SessionSnapshotDto` | finalize; terminal - after this, only `SnapshotUpdated` re-sends of the same finalized state occur |

`SessionFinalized` carries the same DTO as `SnapshotUpdated` with
`state: "Finalized"`; it exists as a distinct event so clients can trigger
the finalize transition (navigate to summary, stop accepting input) without
diffing snapshots.

## Ordering and idempotency

- SignalR guarantees per-connection ordered delivery; because every event
  carries the **full snapshot with a `version`**, clients simply ignore any
  snapshot whose `version` is lower than the one they hold.
- `SnapshotUpdated` coalesces per session on a ~100ms trailing edge: a
  burst of mutations collapses into one broadcast of the latest snapshot.
  Versions still increase strictly, so clients cannot tell the difference;
  it only caps worst-case fan-out (20 connections x 10 gestures/sec x a
  40KB snapshot, uncoalesced, is real bandwidth). Mutation responses are
  never coalesced - a REST caller always gets its own snapshot back.
- All claim gestures are idempotent upserts/deletes - a retried
  `SetShares(x, 2)` converges to the same state.

## Typing on the frontend

`@microsoft/signalr` has no schema generation. The contract above is
hand-mirrored once in `src/lib/realtime/contract.ts` as typed wrappers, and
every inbound payload is parsed with the same Zod schemas used for REST
responses ([08-frontend-design.md](08-frontend-design.md#validation)) -
drift between backend and frontend fails loudly in dev, not silently in
prod.
