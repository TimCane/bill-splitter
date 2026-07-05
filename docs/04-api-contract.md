# REST API contract

Base path `/api/v1`. Controllers, attribute-routed, kebab-case routes,
camelCase JSON (System.Text.Json defaults). OpenAPI served in dev at
`/swagger`; the frontend REST client is generated from it with NSwag
([08-frontend-design.md](08-frontend-design.md#api-client)).

All mutations here are **lifecycle** operations. Claim gestures go over the
hub ([05-realtime-contract.md](05-realtime-contract.md)).

## Auth

- Anonymous: create, resolve short code, join, health, and the snapshot
  `GET` - the unguessable session URL is the credential
  ([10-security-privacy.md](10-security-privacy.md#identity-and-access)),
  and the tokenless visitor states in
  [09-ux-flows.md](09-ux-flows.md#routestaterole-matrix) need to read it.
- Everything else: `Authorization: Bearer {participantToken}`. The token is
  matched by hex SHA-256 against `participants[].tokenHash` in the session
  addressed by the route. There is no cross-session token registry - a
  token only means something inside its own session.
- Host-only endpoints additionally require the matched participant to have
  `isHost: true`; otherwise `403`.

## Endpoints

### `POST /api/v1/sessions`

Create a session from a receipt photo. Multipart form, field `image`,
JPEG or PNG (sniffed by magic bytes, not extension), max 10MB.

- `202 Accepted`

```json
{
  "sessionId": "u3K9mPd2QYqLxN7cWvB4Ag",
  "participantId": "aB3xY9kQ2mN8pL4dF6hJ1w",
  "participantToken": "Zk4...43-char-base64url...Qw",
  "displayName": "Host"
}
```

The creator is participant `Host` (renameable at join screens only for
joiners; the host's name is set via `PUT .../participants/me`, below).
Session starts in `Processing`; the client connects to the hub and waits
for `OcrStatusChanged`.

- `400` bad/missing image, `413` too large, `429` rate limited.

### `GET /api/v1/sessions/{sessionId}`

Full snapshot ([snapshot shape](#sessionsnapshotdto)). Anonymous: holding
the session URL grants read access in every state (this is how a refreshed
tab, a tokenless visitor, or a late viewer of a finalized session
rehydrates).

### `GET /api/v1/sessions/{sessionId}/receipt`

The stored receipt image, `image/jpeg` or `image/png`. Host only, state
`Review` only (the object is deleted at open). `404` `receipt-not-found`
after open.

### `PUT /api/v1/sessions/{sessionId}/participants/me`

Set own display name. Any participant, states `Review` (host fixing their
name) and `Open`. Body: `{ "displayName": "Tim" }` (1-30 chars).
Returns the snapshot.

### Item CRUD - host only, state `Review` only

| Verb + route | Body | Notes |
| --- | --- | --- |
| `POST .../items` | `{ "name": "...", "quantity": 1, "priceMinor": 1250 }` | `201`, snapshot |
| `PUT .../items/{itemId}` | same shape | `200`, snapshot |
| `DELETE .../items/{itemId}` | - | `200`, snapshot |

Validation per [02-domain-model.md](02-domain-model.md#domain-rules):
name 1-80 chars, `quantity >= 1`, `priceMinor >= 0`, max 100 items.

### `PUT /api/v1/sessions/{sessionId}/bill`

Host, `Review` only.

```json
{ "taxMinor": 0, "tipMinor": 500, "serviceMinor": 0, "totalMinor": 5450, "currency": "GBP" }
```

Returns the snapshot. `currency` must be a known ISO 4217 code.

### `POST /api/v1/sessions/{sessionId}/open`

Host, `Review -> Open`. No body. Deletes the receipt image, mints the
short code.

```json
{ "shortCode": "K7MPQ2", "joinUrl": "https://split.example.com/s/u3K9mPd2QYqLxN7cWvB4Ag" }
```

`joinUrl` is built from `App:PublicBaseUrl`
([07-backend-design.md](07-backend-design.md#configuration)); the QR is
rendered client-side from it.

### `GET /api/v1/codes/{shortCode}`

Anonymous. Resolves a typed-in code: `{ "sessionId": "..." }`. `404` if
unknown/expired. Rate limited (brute-force guard,
[10-security-privacy.md](10-security-privacy.md#rate-limits)).

### `POST /api/v1/sessions/{sessionId}/participants`

Join. Anonymous, state `Open` only. Body: `{ "displayName": "Sam" }`.

- `201`

```json
{
  "participantId": "...",
  "participantToken": "...",
  "snapshot": { }
}
```

- `409` not `Open` (still in review, or finalized), `409` participant cap
  (20) reached, `429` rate limited.

Joining is idempotent per person only in the sense that a stored token
skips this call entirely; calling join twice creates two participants.
The client must check localStorage before joining
([08-frontend-design.md](08-frontend-design.md#identity)).

### `POST /api/v1/sessions/{sessionId}/finalize`

Host, state `Open` only. Body: `{ "email": "tim@example.com" }` or `{}`.
Locks claims, splits unclaimed items, shrinks TTL to 1h, broadcasts
`SessionFinalized`, then (if email present) sends the summary in the
background - send failures are logged, never surfaced, and the address is
never stored. Returns the finalized snapshot.

### `GET /healthz`

Anonymous. `200` when the app can reach Redis; body
`{ "redis": true, "minio": true, "ocr": true, "email": false }` with `503`
if any probe fails. `email` is a capability flag, not a probe - `true`
when SMTP is configured ([13-deployment.md](13-deployment.md#environment));
it drives the finalize dialog's email field and never causes `503`. Used
by compose healthchecks and CI smoke tests.

## SessionSnapshotDto

The one read model. Returned by every snapshot-returning endpoint and
carried by `SnapshotUpdated` / `SessionFinalized` hub events. Server-computed
fields marked (c).

```jsonc
{
  "sessionId": "u3K9mPd2QYqLxN7cWvB4Ag",
  "version": 17,
  "state": "Open",                  // Processing | Review | Open | Finalized
  "currency": "GBP",
  "expiresAt": "2026-07-05T19:02:11Z", // (c) from the key's remaining TTL at read time
  "shortCode": "K7MPQ2",            // null until open
  "joinUrl": "https://...",         // (c) null until open
  "hostParticipantId": "aB3x...",
  "ocr": { "status": "Done", "failureReason": null },
  "participants": [
    { "participantId": "aB3x...", "displayName": "Tim", "isHost": true }
  ],
  "items": [
    {
      "itemId": "qW8e...",
      "name": "Margherita",
      "quantity": 1,
      "priceMinor": 1250,
      "claims": [
        { "participantId": "aB3x...", "shares": 1, "allocatedMinor": 1250 } // (c)
      ]
    }
  ],
  "bill": {
    "subtotalMinor": 4950,          // (c) sum of items
    "taxMinor": 0,
    "tipMinor": 500,
    "serviceMinor": 0,
    "totalMinor": 5450,
    "checksumMinor": 0              // (c) subtotal+tax+tip+service-total
  },
  "unclaimedTotalMinor": 1200,      // (c)
  "totals": [                       // (c) one entry per participant
    {
      "participantId": "aB3x...",
      "itemsMinor": 1250,
      "taxMinor": 0,
      "tipMinor": 167,              // 500 weighted by claimed itemsMinor: floor(500*1250/3750) + largest remainder
      "serviceMinor": 0,
      "unclaimedMinor": 0,          // populated only once Finalized
      "totalMinor": 1417
    }
  ]
}
```

Token hashes never appear in any DTO.

## Errors

RFC 7807 `application/problem+json` everywhere, via ASP.NET
`ProblemDetails`. `type` is a stable machine-readable code the frontend
switches on:

| Status | `type` | When |
| --- | --- | --- |
| 400 | `validation` | body/route validation failures (details in `errors`) |
| 401 | `missing-token` | no/garbled bearer token |
| 403 | `not-host` | host-only endpoint, non-host token |
| 403 | `unknown-participant` | token does not match any participant |
| 404 | `session-not-found` | expired or never existed (indistinguishable, deliberately) |
| 404 | `item-not-found` | item id not in session |
| 404 | `receipt-not-found` | receipt image requested after open (deleted) |
| 409 | `wrong-state` | operation not allowed in current state (`detail` names both) |
| 409 | `session-full` | participant cap |
| 413 | `image-too-large` | upload > 10MB |
| 429 | `rate-limited` | includes `Retry-After` |
| 503 | `conflict-retry-exhausted` | CAS gave up after 5 attempts |

## Conventions

- Every snapshot-returning mutation also broadcasts `SnapshotUpdated` to
  the session's hub group - REST callers and hub listeners always converge.
- No `PATCH`; edits are full-resource `PUT`s.
- All timestamps ISO 8601 UTC.
- Request size limit 10MB (the upload); everything else 64KB.
