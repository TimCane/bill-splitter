# Frontend design

Vite SPA per house react-style: React + TypeScript strict, Tailwind CSS +
shadcn/ui + Lucide, TanStack Query, React Router 7, Zod, Vitest + React
Testing Library, pnpm, Prettier. Mobile-first - every screen is designed
for a phone held at a restaurant table; desktop is a centered column.

## Structure

```
frontend/
  src/
    routes/                  # one file per routed screen
      Landing.tsx            # /
      JoinByCode.tsx         # /join
      Session.tsx            # /s/:sessionId  (state-switching container)
    components/
      session/               # ReviewItemList, ClaimList, TotalsBar, ShareCard, ...
      ui/                    # shadcn/ui primitives
    hooks/
      useSession.ts          # snapshot query + hub wiring
      useParticipantToken.ts # localStorage identity
    lib/
      api/
        client.ts            # NSwag-generated REST client (checked in)
        schemas.ts           # Zod schemas for SessionSnapshotDto etc.
      realtime/
        connection.ts        # SignalR connection factory
        contract.ts          # typed hub method/event wrappers
      money.ts               # formatMinor(amountMinor, currency) via Intl.NumberFormat
      image.ts               # canvas downscale + JPEG re-encode
```

## Routes

| Route | Screen | Notes |
| --- | --- | --- |
| `/` | Landing/Capture | photo picker, creates session |
| `/join` | Code entry | 6-char short code -> resolve -> `/s/:id` |
| `/s/:sessionId` | Session | one container that switches on snapshot `state` and role: Processing, Review (host), Share (host), Claim, Summary |

`Session.tsx` renders by `(state, isHost, hasToken)` - deep links, refreshes
and QR scans all land here and self-sort. A visitor with no token while
`Open` sees the join (name) prompt; while `Finalized`, a read-only summary;
while `Review`/`Processing` (not host), a "session not open yet" holding
card. Full matrix in [09-ux-flows.md](09-ux-flows.md).

## Identity

`useParticipantToken(sessionId)`:

- localStorage key `bs:{sessionId}` holding
  `{ participantId, participantToken }`, written after create/join,
  read on mount. Per-session key = being in two sessions on one phone
  works.
- No token + `Open` -> join flow. No token + anything else -> spectator
  states above.
- localStorage entries for expired sessions are pruned opportunistically on
  Landing mount (any entry older than 25h by a stored timestamp).

## Server state

One source of truth: the TanStack Query cache entry
`['session', sessionId]` holding the latest `SessionSnapshotDto`.

- Seed: REST `GET /sessions/{id}` on mount.
- Live: the SignalR connection's `SnapshotUpdated` / `SessionFinalized`
  handlers call `queryClient.setQueryData(['session', id], snapshot)` -
  after a **version guard**: ignore snapshots with `version <` cached
  version ([05-realtime-contract.md](05-realtime-contract.md#ordering-and-idempotency)).
- Components read via `useSession(sessionId)` and render; there is no
  client-side totals math anywhere (`money.ts` only formats).
- Claim gestures call hub methods and do **not** optimistically update -
  round-trip is <100ms on the same wifi and the authoritative snapshot
  arrives as fast as an optimistic paint would meaningfully beat.

Hub connection lifecycle lives in `useSession`: connect on mount (token
present), `withAutomaticReconnect`, dispose on unmount. Connection status
(`connected | reconnecting | disconnected`) is exposed for the status pill
([09-ux-flows.md](09-ux-flows.md#7-claim---state-open-the-main-screen-everyone)).

## API client

- REST: NSwag-generated TypeScript client from the backend OpenAPI spec,
  checked in at `src/lib/api/client.ts`; CI regenerates and fails on diff
  ([12-ci.md](12-ci.md)) so contract drift is caught at PR time.
- Realtime: hand-written wrappers in `lib/realtime/contract.ts` (SignalR
  has no codegen).

### Validation

Every payload that crosses the wire inbound - REST responses and hub
events - is parsed with the Zod schemas in `lib/api/schemas.ts`
(`SessionSnapshotSchema` being the big one). Parse failure = a thrown
error in dev, a logged warning + best-effort render in prod.

## Uploads

`lib/image.ts`: `preprocess(file: File): Promise<Blob>` - decode via
`createImageBitmap`, downscale so the longest edge is <= 2000px, re-encode
JPEG quality 0.85. Always re-encodes (kills HEIC before it reaches the
server) and strips EXIF as a side effect. Upload the blob as multipart
`image` with upload progress from `XMLHttpRequest` (fetch has no upload
progress).

## QR

Client-rendered from `joinUrl` with the `qrcode` npm package on the host's
Share card. No server-side image generation.

## Conventions (house react-style, restated where this repo relies on them)

- Named exports only; PascalCase component files.
- `@/` import alias for `src/`.
- `type` over `interface`; no `any`; no non-null assertions.
- Server state only in TanStack Query; local UI state via
  `useState`/`useReducer`.
- Tailwind utilities; tokens in the Tailwind config; shadcn/ui for
  primitives (Button, Card, Input, Sheet, Badge, Alert).
- Prettier: single quotes, no semicolons, 2-space.
- Tests: Vitest + RTL for components/hooks
  ([11-testing-strategy.md](11-testing-strategy.md#frontend-vitest--rtl));
  Puppeteer
  for the two-browser e2e flows.
