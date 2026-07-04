# ADR-0002: SignalR hub for claims, REST for lifecycle

Status: accepted, 2026-07-04

## Context

Mutations split into two natures: rare lifecycle operations (create,
review edits, join, open, finalize - some with uploads or token issuance)
and high-frequency claim gestures fired repeatedly from every phone at the
table. House style favors controllers + DTOs + NSwag for everything; a
realtime app favors the socket it already holds.

## Decision

REST owns every lifecycle operation. The hub owns exactly three methods -
`ClaimItem`, `UnclaimItem`, `SetShares` - plus all server->client events.
Join stays REST because it mints the token the hub connection then
authenticates with.

## Consequences

- Two mutation pipelines exist; the hub one is kept minimal (three methods,
  one validation table) to bound the cost.
- Claim gestures ride the already-open connection: no per-tap HTTP
  overhead, and gesture throttling happens per-connection in the hub.
- NSwag covers the whole REST surface; the small hub contract is
  hand-mirrored in TypeScript and Zod-validated at runtime.
- Every mutation, REST or hub, converges on the same snapshot broadcast -
  clients cannot tell which channel caused an update.
