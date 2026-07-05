# Build order

Seven milestones, dependency-ordered - the order is fixed, the sizes are
not. Each lists scope, the docs it implements, and concrete acceptance
criteria. A milestone is done when its criteria pass and CI is green;
finish one before starting the next.

## M1 - Scaffold

Scope: repo skeleton and the walking skeleton of every process.

- `backend/`: solution per [07-backend-design.md](07-backend-design.md)
  (empty domain, `/healthz` probing Redis+MinIO+OCR), Directory.Build.props,
  central packages, editorconfig.
- `frontend/`: Vite scaffold per [08-frontend-design.md](08-frontend-design.md),
  Tailwind + shadcn/ui + router + query wired, placeholder Landing.
- `ocr/`: FastAPI app with `/healthz` and a stubbed `/ocr` (echoes fixture
  JSON behind an env flag; real PaddleOCR wiring lands here too if quick).
- Devcontainer compose gains the `ocr` service
  ([06-ocr-service.md](06-ocr-service.md#compose-service)); CI per
  [12-ci.md](12-ci.md) (all jobs, e2e job stubbed).

Accept:
- [ ] `docker compose up` in the devcontainer: all four services healthy
- [ ] `GET /healthz` returns all-true through the backend
- [ ] CI green on a PR touching all three trees

## M2 - Session core

Scope: the aggregate, Redis store with Lua CAS, MinIO storage
(`IReceiptStorage` - create needs it), tokens/auth, create (image accepted
and stored, OCR faked as instant-empty-`Review`), join, snapshot GET,
rename. [02](02-domain-model.md), [03](03-redis-schema.md),
[04](04-api-contract.md) minus OCR/finalize.

Accept:
- [ ] Full auth matrix integration tests pass (wrong token / non-host / not-Open)
- [ ] Two parallel joins never corrupt the doc (CAS test)
- [ ] Session TTL visible via `redis-cli TTL`; expired session -> 404 `session-not-found`
- [ ] `SplitCalculator` unit + property tests pass (written now, used in M4)

## M3 - OCR pipeline

Scope: bounded queue + `OcrWorker`, real sidecar inference,
`ReceiptParser` + fixture corpus, `OcrStatusChanged` over the hub (hub
exists connect+events only). [06](06-ocr-service.md),
[05](05-realtime-contract.md) events.

Accept:
- [ ] Photo of a real receipt -> `Review` with plausible items in < 30s
- [ ] Seed fixture corpus (7 receipts, [11-testing-strategy.md](11-testing-strategy.md#receiptparser)) passes
- [ ] Sidecar down -> session lands `Review`/`Failed` with reason; no hang
- [ ] 17 concurrent uploads: 16 queue, overflow gets 429, none pin CPU beyond 2 workers

## M4 - Review gate

Scope: item CRUD + bill endpoints, checksum, receipt image endpoint, open
(image delete + short code + join URL), Review UI per
[09-ux-flows.md](09-ux-flows.md#4-review-host-gate---state-review), Share sheet, code
resolve + `/join`.

Accept:
- [ ] Host can fix a misparse end-to-end on a phone-sized viewport
- [ ] Checksum banner: green/amber/failed variants render with the spec copy
- [ ] `POST /open`: MinIO object gone (asserted), receipt endpoint 404s, code resolves
- [ ] Non-host cannot see review or receipt (403)

## M5 - Realtime claims

Scope: hub gestures + per-connection throttle, snapshot broadcast on every
mutation, Claim screen with claims/shares/steppers/totals drawer/connection
pill, join prompt, version-guarded client cache.
[05](05-realtime-contract.md), [08](08-frontend-design.md),
[09-ux-flows.md](09-ux-flows.md#7-claim---state-open-the-main-screen-everyone).

Accept:
- [ ] Two browsers: claim visible on the other in < 1s
- [ ] Shared item with shares 2:1 shows exact largest-remainder amounts on both
- [ ] Concurrency integration test (100 parallel `SetShares`) consistent
- [ ] Kill/restore network on one client: pill reconnects, snapshot heals
- [ ] Refresh mid-session resumes identity from localStorage without rejoining

## M6 - Finalize and email

Scope: finalize endpoint (unclaimed split, lock, TTL shrink,
`SessionFinalized`), Summary screen with countdown, MailKit sender +
summary template, email-optional capability flag.
[04](04-api-contract.md#post-apiv1sessionssessionidfinalize),
[09-ux-flows.md](09-ux-flows.md#8-summary---state-finalized).

Accept:
- [ ] Post-finalize invariant holds: per-person totals sum to the full bill (property test + e2e assertion)
- [ ] Claim gesture after finalize -> soft `wrong-state` handling (refresh to summary, no error toast)
- [ ] Email received via a real relay from a dev box; address absent from Redis (`redis-cli --scan` + dump grep) and from logs
- [ ] Session and code keys report ~1h TTL after finalize

## M7 - Hardening

Scope: rate limit policies, upload sniffing + decode-bomb guard, security
headers, caps enforcement, no-PII log audit, Puppeteer e2e specs, prod
compose file + `.env.example`, deploy to a real box.
[10](10-security-privacy.md), [11](11-testing-strategy.md#end-to-end-puppeteer),
[13](13-deployment.md).

Accept:
- [ ] Both e2e specs green against the full compose stack in CI
- [ ] Rate limits verified with a scripted burst (429 + `Retry-After`)
- [ ] PNG-renamed-to-JPG rejected; 40MB upload rejected with 413
- [ ] Deployed instance passes the two-phone test over real phones + TLS
- [ ] One hour after a finalized real session: `redis-cli --scan` empty, MinIO bucket empty

## Explicitly not scheduled

Payments, multi-receipt, PWA, i18n, host transfer, presence, receipt
re-upload, SignalR backplane ([00-overview.md](00-overview.md#out-of-scope-mvp)).
If any of these feels necessary mid-build, it goes to a design conversation
first, not into a milestone.
