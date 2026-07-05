# Architecture

## Components

```
                 ┌──────────────────────────────────────────────┐
                 │                  frontend/                   │
                 │      React + Vite SPA (served statically)    │
                 └──────┬───────────────────────────┬───────────┘
                        │ REST /api/v1/*            │ SignalR /hubs/session
                        ▼                           ▼
                 ┌──────────────────────────────────────────────┐
                 │                  backend/                    │
                 │   ASP.NET Core (.NET 10), single instance    │
                 │   controllers + SessionHub + OCR queue       │
                 └──┬─────────────┬──────────────┬──────────────┘
                    │ HTTP        │              │ SMTP (finalize only)
                    ▼             ▼              ▼
              ┌──────────┐  ┌──────────┐   ┌──────────────┐
              │   ocr/   │  │  redis   │   │ SMTP relay   │
              │ PaddleOCR│  │ sessions │   │ (external)   │
              │ FastAPI  │  │ 24h TTL  │   └──────────────┘
              └──────────┘  └──────────┘
                    ▲
                    │ image bytes (fetched by backend)
              ┌──────────┐
              │  minio   │
              │ receipts │
              └──────────┘
```

- **frontend/** - Vite SPA. Static files; no server rendering. Talks REST
  for lifecycle mutations and listens/talks on one SignalR connection per
  session. See [08-frontend-design.md](08-frontend-design.md).
- **backend/** - ASP.NET Core. Owns all state transitions, all totals math,
  and the only Redis/MinIO/OCR/SMTP credentials. See
  [07-backend-design.md](07-backend-design.md).
- **ocr/** - Python FastAPI sidecar wrapping PaddleOCR. Stateless: image in,
  text lines with bounding boxes out. Reachable only on the internal
  network. See [06-ocr-service.md](06-ocr-service.md).
- **redis** - the only session store. Run with persistence disabled
  (ephemeral by design, [ADR-0004](adr/0004-ephemeral-by-design.md)).
- **minio** - receipt image, upload until end of host review. 1-day bucket
  lifecycle rule as a safety net.

## Principles

1. **Server is the single source of truth.** Every mutation recomputes the
   full session snapshot server-side and broadcasts it; clients only render.
   No totals math in TypeScript.
2. **Full snapshot, not deltas.** A session is a few KB. Broadcasting the
   whole `SessionSnapshotDto` on every change eliminates ordering bugs and
   makes reconnect free (the next snapshot heals everything). Bursts
   coalesce per session ([05-realtime-contract.md](05-realtime-contract.md#ordering-and-idempotency)).
3. **REST for lifecycle, hub for claims.** Session creation, review edits,
   join, and finalize are REST endpoints. Only the high-frequency claim
   gestures (`ClaimItem`, `UnclaimItem`, `SetShares`) are hub methods
   ([ADR-0002](adr/0002-hub-claims-rest-lifecycle.md)).
4. **Ephemeral by default.** Every Redis key carries a TTL from the moment
   it is written. Deletion is never a cleanup job that can be forgotten.

## Session state machine

```
                    upload
                      │
                      ▼
               ┌────────────┐   OCR done or failed   ┌────────────┐
               │ Processing │ ─────────────────────► │   Review   │
               └────────────┘                        └─────┬──────┘
                                                           │ host: open
                                                           │ (image deleted)
                                                           ▼
               ┌────────────┐   host: finalize      ┌────────────┐
               │ Finalized  │ ◄──────────────────── │    Open    │
               └────────────┘                       └────────────┘
```

| State | Who can access | Allowed operations |
| --- | --- | --- |
| `Processing` | host | `GET` snapshot (poll-free: `OcrStatusChanged` over hub) |
| `Review` | host | item CRUD, bill edit, rename self, view receipt image, open |
| `Open` | host + joiners | join, rename self, claim/unclaim/set-shares, finalize (host) |
| `Finalized` | anyone with the link | `GET` snapshot (read-only) until TTL |

The access column governs mutations and the receipt image; the snapshot
`GET` itself is anonymous in every state - the unguessable URL is the
credential ([04-api-contract.md](04-api-contract.md#auth)).

- OCR failure does **not** create a dead state: the session lands in
  `Review` with `ocr.status = "Failed"` and an empty item list; the host
  enters items manually or abandons (TTL cleans up).
- There is no transition out of `Finalized` and no way back from `Open` to
  `Review`.
- Every state transition and every mutation is guarded by the state table
  above; out-of-state calls fail `409 Conflict`
  ([04-api-contract.md](04-api-contract.md#errors)).

## Receipt image lifecycle

| Moment | Action |
| --- | --- |
| Upload (`POST /api/v1/sessions`) | stored at `receipts/{sessionId}.jpg` in MinIO |
| OCR job | backend streams object to the OCR sidecar |
| Review | host may view it via `GET .../receipt` |
| Open (`POST .../open`) | object deleted |
| Safety net | bucket lifecycle rule expires objects after 1 day |

## Cross-cutting flows

**Upload -> Review** (host):
1. `POST /api/v1/sessions` (multipart image) -> `202` with `sessionId` +
   host `participantToken`; session written to Redis in `Processing`.
2. Backend enqueues an OCR job on a bounded in-process channel
   (concurrency 2).
3. Worker: fetch image from MinIO -> `POST` to sidecar -> parse lines into
   items ([06-ocr-service.md](06-ocr-service.md#parsing)) -> CAS-write
   session as `Review` -> broadcast `OcrStatusChanged`.

**Claim** (participant):
1. Hub `SetShares(itemId, shares)` with the participant identified by their
   token (attached at connect time).
2. Backend validates state `Open`, applies the claim via Lua CAS
   ([03-redis-schema.md](03-redis-schema.md#concurrency)), recomputes all
   totals, broadcasts `SnapshotUpdated` to the session group.

**Finalize** (host):
1. `POST .../finalize` with optional `email`.
2. Backend: split unclaimed items equally, lock claims, state ->
   `Finalized`, shrink TTL to 1h, broadcast `SessionFinalized`.
3. If an email was given: render summary, send via SMTP in the background.
   Email failure is logged as exception type + SMTP status code only (SMTP
   error messages embed the recipient address), never blocks finalize; the
   address is never written to Redis.
