# Overview

Ephemeral bill splitting for a table of people paying a restaurant
individually. The host scans the receipt, everyone claims their items from
their own phone, totals reconcile to the penny, and all data is deleted as
soon as possible. No accounts, no database, no persistent data.

## The flow in one paragraph

Host photographs the receipt. The backend OCRs it (self-hosted PaddleOCR
sidecar) and parses it into line items. The host corrects OCR mistakes behind
a review gate, then opens the session; the receipt image is deleted at that
moment. Other people at the table scan a QR code to join, claim the items
they had (shared items split by integer shares), and watch everyone's
running totals update live over SignalR. Tax/tip/service are allocated
pro-rata to claimed subtotals. The host finalizes; unclaimed items split
equally across everyone, claims lock, and an optional summary email goes to
an address the host types at that moment. The session expires from Redis one
hour later.

## Hard constraints

- **No persistence.** Redis is the only session store (24h TTL, 1h after
  finalize). MinIO holds the receipt image only between upload and the end
  of host review. No Postgres, no EF Core.
- **No accounts.** Identity is a server-issued token in localStorage plus a
  self-chosen display name. Possession of the session URL is the auth.
- **No payments.** The app reports what each person owes; it never moves
  money.
- **Self-hosted OCR.** The receipt image never leaves our infrastructure
  ([ADR-0001](adr/0001-self-hosted-ocr.md)).
- **Single instance.** One backend process; no SignalR backplane until
  there is a reason ([13-deployment.md](13-deployment.md)).

## Out of scope (MVP)

- Payments / payment links
- Multi-receipt sessions (one receipt per session; run two if needed)
- PWA / offline support
- i18n (currency display is locale-aware, UI is English)
- Host transfer (host device loss = session expires at TTL)
- Presence indicators (who is online)
- Receipt re-upload (bad photo = start a new session or enter items by hand)

## Known risks

- **OCR accuracy is the project risk.** Expect ~70-85% line accuracy on
  real thermal receipts; the review gate is the product, not polish. Grow
  the parser fixture corpus from real receipts from day one
  ([11-testing-strategy.md](11-testing-strategy.md#receiptparser)).
- **Retroactive totals.** Another claimant joining an item changes other
  people's numbers live. Correct by design, but the UI must animate the
  change so it reads as intentional, not as a bug.

## Document map

| Doc | Contents |
| --- | --- |
| [01-architecture.md](01-architecture.md) | Components, data flow, session state machine |
| [02-domain-model.md](02-domain-model.md) | Entities, money rules, split math |
| [03-redis-schema.md](03-redis-schema.md) | Keys, document shape, concurrency, TTLs |
| [04-api-contract.md](04-api-contract.md) | REST endpoints, DTOs, errors, auth |
| [05-realtime-contract.md](05-realtime-contract.md) | SignalR hub methods and events |
| [06-ocr-service.md](06-ocr-service.md) | Sidecar contract, parsing heuristics |
| [07-backend-design.md](07-backend-design.md) | Solution layout, services, DI, config |
| [08-frontend-design.md](08-frontend-design.md) | Stack, routes, state, hub integration |
| [09-ux-flows.md](09-ux-flows.md) | Screens, wireframes, element inventory, copy |
| [10-security-privacy.md](10-security-privacy.md) | Tokens, rate limits, abuse controls, ephemerality |
| [11-testing-strategy.md](11-testing-strategy.md) | Unit / integration / e2e split, fixtures |
| [12-ci.md](12-ci.md) | GitHub Actions pipelines, merge gates |
| [13-deployment.md](13-deployment.md) | Production topology, env reference |
| [14-build-order.md](14-build-order.md) | Milestones with acceptance criteria |
| [15-receipt-parsing.md](15-receipt-parsing.md) | Parser detector/rule/score reference |
| [adr/](adr/) | Decision records for the calls that shaped this design |
