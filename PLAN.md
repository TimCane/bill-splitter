# Bill Splitter — Plan

> Superseded by [docs/](docs/00-overview.md), which carries the full,
> authoritative design. This file is the original one-page plan, kept for
> the narrative; where they disagree, docs/ wins.

Ephemeral bill splitting for a table of people paying a restaurant
individually. Host scans the receipt, everyone claims their items from their
own phone, totals reconcile to the penny, everything is deleted as soon as
possible. No accounts, no persistent data.

## Product flow

1. Host photographs the receipt. OS camera capture
   (`<input type="file" accept="image/*" capture="environment">`), client
   canvas-downscales to ~2000px longest edge and JPEG-compresses before
   upload. Preview + retake screen.
2. Upload returns `202` with a new session id. Client lands on the review
   page and watches `ocr-status` events (processing → done/failed) over
   SignalR.
3. OCR runs in a PaddleOCR sidecar; rules-based parsing turns text+boxes into
   line items (name, qty, price) plus tax/tip/service and total.
4. **Host review gate.** Editable item list beside the receipt photo, with a
   checksum banner (items + tax vs printed total). Host fixes names/prices,
   adds missed lines, deletes junk rows, confirms currency. Session opens to
   joiners only after confirmation. The receipt image is deleted from MinIO
   at that moment.
5. Joiners scan a QR encoding `https://app/s/{128-bit-random-id}` (6-char
   fallback code shown beside it). Anyone with the link joins until finalize;
   nickname prompt on join. Possession of the QR is the auth — no PIN, no
   lobby.
6. Claiming: multiple people can claim the same item; price divides by
   integer shares (default 1 per claimant, adjustable for "I had 2 of the 3
   beers"). Tax/tip/service is allocated pro-rata to each person's claimed
   subtotal. Everyone sees everyone's claims and running totals live.
7. Host finalizes whenever the table is done. Unclaimed items split equally
   across all participants as a visible "unclaimed" bucket, so per-person
   totals always sum to the printed bill. Claims lock; late arrivals see a
   read-only summary.
8. Optional email: host types their address at finalize (not earlier).
   Backend sends the summary via a transactional provider and never writes
   the address to Redis — it exists only in the finalize request. Caveat to
   document: the provider retains delivery logs, so "no persistent data"
   means none on our side.

## Architecture

- **Backend**: ASP.NET Core (.NET 10) + SignalR. WebSockets with per-session
  groups; claims go up and fan out over the same connection. Single instance
  — no Redis backplane until there's a reason (it's a config flip later).
- **Frontend**: React + Vite SPA, strict TS, Tailwind, TanStack Query.
- **OCR**: PaddleOCR (PP-OCRv4) in a FastAPI sidecar on the compose network.
  Backend POSTs the image, gets text + bounding boxes back. Bounded to 1–2
  concurrent jobs, queued behind that. Parsing heuristics: right-aligned
  price column, qty prefixes, keyword rows for TAX/TIP/TOTAL.
- **State**: Redis is the only session store (MinIO transiently holds the
  receipt image). Server is the single source of truth:
  every mutation recomputes and broadcasts the full session snapshot (items,
  claims, per-person totals) — a few KB, so no deltas, no client-side totals
  math, reconnect is free.
- **Uploads**: MinIO, `bill-splitter` bucket. Object lives only from upload
  until the host closes the review gate.
- **Repo**: monorepo — `backend/`, `frontend/`, `ocr/`.

## Data model decisions

- Money is integer minor units (cents) everywhere, including the wire. One
  currency per session, defaulted at review, display-only.
- Share math in integers with largest-remainder distribution so every split
  sums exactly — no floating point, no orphaned pennies.
- Participant identity: server-issued random token in localStorage + chosen
  display name. Token is the identity (nickname collisions fine); refresh or
  reconnect resumes the same participant. Host is a flag on the session
  creator.
- Host device loss: token survives refresh/crash on the same phone. If the
  device is truly gone there is no transfer — the session expires at TTL and
  everyone can still read their own totals to settle manually.

## Ephemerality

- Receipt image: deleted from MinIO when the host confirms the review gate.
  Bucket gets a 1-day lifecycle rule as a safety net for orphaned uploads.
- Session state: 24h absolute TTL in Redis, shrunk to 1h at finalize so
  the summary stays readable before it self-destructs.
- Email address: never stored; held only in the finalize request.

## Abuse controls

Public, unauthenticated image upload + OCR is the attack surface. These
shape the API, so they land in v1, not later:

- Per-IP rate limits on session creation and upload.
- ~10MB upload cap; server-side image validation.
- OCR concurrency bound (1–2 jobs) so a burst can't pin the CPU.
- ~20 participants per session; cap sessions per IP.
- Finalize is host-only — closes the "joker locks everyone's claims" vector.

## Out of scope (MVP)

- Payments / payment links — the app says what you owe, never moves money.
- Multi-receipt sessions — one receipt per session; run two if needed.
- PWA / offline — it's a live shared session.
- i18n — but currency display is locale-aware from day one (cheap).
- Host transfer UI.

## Known risks

- **OCR accuracy is the project risk.** PaddleOCR + heuristics will land
  ~70–85% line accuracy on real thermal receipts. The review gate is the
  product, not polish. Collect real receipts early and build parser test
  fixtures from them.
- Retroactive totals: a second claimant joining an item changes other
  people's numbers live. Correct by design, but the UI must flag/animate the
  change so it doesn't read as a bug.

## Devcontainer

The PaddleOCR sidecar joins `docker-compose.yml` when the `ocr/` service is
scaffolded — a compose service without its build context would break
`docker compose up` for everyone until then.
