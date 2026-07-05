# Security and privacy

Threat model: a public, unauthenticated web app whose worst-case assets are
one restaurant receipt image (briefly), first names, and one email address
(in-flight only). The defenses are sized to that - abuse of the compute
(OCR) and nuisance within a session matter more than data theft.

## Identity and access

- **Session URL is the credential.** `sessionId` = 16 bytes of crypto
  randomness, base64url (22 chars) - unguessable. Anyone holding the URL
  can read the snapshot in any state and join while `Open`; that is the
  product's trust model (the QR sits on a physical table).
- **Participant tokens**: 32 bytes crypto random, base64url (43 chars),
  issued once at create/join, stored client-side in localStorage and
  server-side only as a hex SHA-256 hash. A leaked Redis dump therefore
  exposes no usable tokens.
- Tokens are scoped to their session; there is nothing to escalate to.
- **Host powers** (review edits, open, finalize, receipt view) require the
  `isHost` participant - enforced by policy on controllers and checked in
  the hub.
- **Short codes** (`code:{shortCode}`) are low-entropy by design; the
  resolve endpoint is the guarded surface (rate limit below), and codes die
  with their session.

## Rate limits (ASP.NET rate limiter, per client IP)

| Policy | Applies to | Limit |
| --- | --- | --- |
| `create-session` | `POST /sessions` | 5 / hour |
| `join` | `POST .../participants` | 20 / min |
| `resolve-code` | `GET /codes/{code}` | 10 / min, and 100 / day |
| `global` | all REST | 100 / min |
| hub gestures | `ClaimItem`/`UnclaimItem`/`SetShares` | 10 / sec per connection, enforced in the hub (rate limiter middleware does not see hub invocations) |

429 responses carry `Retry-After`. Behind the reverse proxy, client IP
comes from `X-Forwarded-For` via `ForwardedHeadersOptions` restricted to
the proxy's address ([13-deployment.md](13-deployment.md)).

## Upload hardening

- 10MB request cap (Kestrel + explicit check).
- Magic-byte sniffing: accept only JPEG (`FF D8 FF`) / PNG
  (`89 50 4E 47`); content-type header is not trusted.
- Decode bomb guard: reject images whose header dimensions exceed
  8000x8000 before full decode (checked in the OCR sidecar as well).
- The image is stored privately in MinIO and only ever served back to the
  host over the authenticated receipt endpoint - never a public URL.
- OCR queue is bounded (capacity 16, concurrency 2): a flood of uploads
  gets 429s, not a pinned CPU.

## Session-level nuisance controls

Participants are pseudonymous and physically present; the griefing surface
is small but real:

- Claim gestures only mutate **your own** claims - there is no way to
  unclaim someone else's items or rename another participant.
- Caps: 20 participants, 100 items, name <= 30 chars, shares 1-99.
- Finalize is host-only, closing the lock-everyone-out-early vector.
- Not defended (accepted): a participant claiming items dishonestly. The
  table can see who claimed what; social pressure is the mechanism.

## Ephemerality guarantees

| Data | Written | Gone by | Mechanism |
| --- | --- | --- | --- |
| Receipt image | upload | host opens session | explicit MinIO delete; 1-day bucket lifecycle as backstop |
| Session doc (names, items, claims) | create | 24h, or finalize + 1h | Redis TTL, set at write time, never absent |
| Email address | never persisted | end of finalize request | exists only in the request + SMTP handshake |
| Server logs | continuous | log rotation | **rule**: log ids and states, never display names, never email addresses, never image bytes |

Redis runs with `save ""` and `appendonly no` - nothing ever touches disk.
An honest limitation to state anywhere ephemerality is advertised: the
summary email, once sent, lives in the recipient's mailbox and the SMTP
provider's delivery logs; and receipt images transit process memory. "No
persistent data" means our infrastructure retains nothing.

## Transport and headers

- TLS terminated at the reverse proxy; HSTS on.
- Same-origin SPA -> no CORS in production; cookies are not used at all
  (no CSRF surface - auth is a bearer token from localStorage).
- Standard header set: `X-Content-Type-Options: nosniff`,
  `Referrer-Policy: no-referrer`, CSP `default-src 'self'` +
  `connect-src 'self' wss:` (allow the WebSocket), `img-src 'self' data:`
  (QR data URI, receipt blob).
- SignalR `access_token` in the query string is standard but ends up in
  proxy access logs - mitigated by tokens being per-session, short-lived,
  and useless without the session; do not log query strings at the proxy
  ([13-deployment.md](13-deployment.md)).

## Secrets

Redis/MinIO/SMTP credentials via environment only
([07-backend-design.md](07-backend-design.md#configuration)). Nothing
secret in the repo; `.env.example` documents every variable with dummy
values ([13-deployment.md](13-deployment.md#environment)).
