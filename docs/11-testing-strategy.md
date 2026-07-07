# Testing strategy

House split: xUnit + Moq + FluentAssertions for unit tests; Testcontainers
+ `WebApplicationFactory` for integration; Vitest + RTL on the frontend;
Puppeteer for e2e. What follows is what to test in *this* app, not a
testing tutorial.

## Backend unit (`BillSplitter.Tests`)

The two pure services carry the correctness of the whole product; they get
the deepest coverage in the repo.

### `SplitCalculator`

- Largest-remainder cases: exact division, remainders, tie-breaking by
  index, single claimant, all-equal weights.
- Property-based (FsCheck) over random sessions, asserting the invariants
  from [02-domain-model.md](02-domain-model.md#invariants-property-test-these):
  allocations sum exactly, nothing negative, deterministic on re-run,
  post-finalize whole-bill coverage.
- Named regression cases: extras with zero claimed subtotals, nobody
  claimed anything at finalize (equal split), 1p items among 3 claimants.

### `ReceiptParser`

Fixture-driven: each fixture is a recorded OCR sidecar JSON response plus
the expected `ParsedReceipt` JSON, under
`tests/BillSplitter.Tests/Fixtures/receipts/{name}/(ocr.json|expected.json)`.
One parameterized test runs the whole corpus.

- **Grow the corpus from real receipts from day one** - every OCR
  misparse found during development becomes a fixture before it is fixed.
  This corpus is the parser's real spec
  ([known risk](00-overview.md#known-risks)).
- The corpus is also the spec for the pipeline extraction (ADR-0006 Phase A):
  each concern pulled out of the engine - the `BasicNormalizer` line normalizer,
  the `KeywordClassifier` line classifier, the `ItemSelectionEngine` item rules
  and the `BillDetectionEngine`/`GrandTotalDetector` bill detectors - keeps the
  corpus byte-identical green, so no per-layer unit tests are added.
- Multi-line handling has its own hand-authored fixtures: `burger-modifiers`
  exercises the `ModifierMerger` pre-pass (an item followed by `+ Bacon` /
  `No Onion` and another with `Extra Sauce`), asserting the modifiers land on the
  right item names with prices unchanged, while the `No payment received` corpus
  fixtures keep proving the pre-pass leaves payment-status footers alone.
- The pipeline's in-memory parse-decision trace (ADR-0006 Phase A, `ParseDecision`)
  has its own corpus-backed test, `ReceiptParseTraceTests`: it reads the trace off
  the internal `ReceiptParseEngine.ParseTraced` (exposed via `InternalsVisibleTo`)
  and asserts the deciding rule and score for a fixture line - e.g. the reconciling
  unit-price column rule winning `2 Roast Beef 27.00 54.00`. The trace never rides
  the public `ParsedReceipt` and is never logged, so no receipt text leaves the
  process (docs/06-ocr-service.md#parsing, docs/15-receipt-parsing.md#diagnostics).
- Phase B capabilities each land with their own fixtures: `wrapped-item-names`
  proves the wrapped-name pre-pass (a name split over `Classic` / `BAO` / `£6.50`
  folded into one item) while the already-inline corpus stays byte-for-byte green.
  `wrapped-names-edge-cases` pins the fold's geometry: a centred store header
  above a single-line item is flushed not swallowed, a name wrapped to three
  lines folds whole, and a VAT-class-coded price (`£5.00 B`) still merges.
- Seed set to create at milestone 3: clean UK card receipt, US receipt
  with TAX+TIP lines, quantity lines (`2x`, `2 @ 5.50`), service charge,
  dot leaders, a deliberately blurry photo (low confidence), a non-receipt
  photo (should yield zero items + warnings, not garbage).

**Recording a fixture** (repeatable loop, docs/06-ocr-service.md#preprocessing):

1. Drop the receipt image next to a running sidecar and record its response:
   `python -m tools.record_fixture <image> <fixture-name>` (from `ocr/`). This
   writes `Fixtures/receipts/<fixture-name>/ocr.json` and copies the image in as
   `receipt.jpg`.
2. Hand-author `<fixture-name>/expected.json` - the ground-truth `ParsedReceipt`,
   money in integer minor units.
3. Run `ReceiptParserCorpusTests`. Fix `ReceiptParser` for parser misses; fix the
   sidecar preprocessing and **re-record** `ocr.json` for raw-text misreads.
   Repeat until green.

Fixture images are permanent repo history - use only receipts free of personal
data (no cardholder names, full card numbers, emails, phones): synthetic,
self-owned or public. Raw misreads that no preprocessing recovers are parked as
`Warnings`, never silently dropped.

### `Session` aggregate

State-machine table tests: every mutation x every state -> allowed or the
expected `DomainException` code. Caps, share bounds, host-only rules.

## Backend integration (`BillSplitter.IntegrationTests`)

Testcontainers spin up real `redis:7-alpine` and `minio` per collection;
`WebApplicationFactory` hosts the API with the OCR sidecar replaced by a
`WireMock.Net` stub returning fixture OCR JSON (the real sidecar is too
heavy for CI; its own contract is covered by the e2e smoke).

Scenarios (each is one test walking real HTTP + a real SignalR client):

1. **Full happy path**: create (fixture image) -> OCR stub -> Review ->
   edit an item -> open -> join x2 -> claims + `SetShares` over the hub ->
   snapshots observed on all clients -> finalize -> totals correct, TTL
   shrunk (assert with `TTL` command), image gone from MinIO.
2. **Concurrency**: two hub clients hammer `SetShares` on the same item in
   parallel (100 iterations); final snapshot is consistent, no 5xx, CAS
   retries observed via metrics/log assertions.
3. **Auth matrix**: wrong token, non-host on host endpoints, hub connect
   with bad token, join when not `Open`.
4. **OCR failure path**: stub returns 500 -> session lands in `Review` +
   `Failed`, manual item entry proceeds to a working session.
5. **Expiry**: session with 1s TTL -> `404 session-not-found`, hub gets
   `session-not-found`.
6. **Limits**: 413 oversized upload, magic-byte rejection, participant cap,
   rate limiter 429 (tight test-config limits).

## Frontend (Vitest + RTL)

- `money.ts` formatting per currency/locale.
- `image.ts` preprocessing (canvas mocked; asserts target dimensions and
  JPEG output type).
- Zod schema round-trips against DTO fixtures **exported from the backend
  integration tests** (shared `fixtures/dtos/` folder at repo root) - the
  cross-app contract test.
- `Session.tsx` state/role matrix: given a mocked snapshot per
  `(state, isHost, hasToken)` cell, the right screen renders
  ([09-ux-flows.md](09-ux-flows.md#routestaterole-matrix)).
- Claim row interaction: tap -> hub method called; snapshot update ->
  share amounts re-render (hub mocked at the `contract.ts` boundary).

## End-to-end (Puppeteer)

Runs against the full compose stack (real OCR sidecar, real Redis/MinIO) -
the only place the Python service is exercised. Two spec files, kept
deliberately small (e2e is smoke, not coverage):

1. **Two-phone split**: browser A creates a session from a fixture image,
   reviews, opens; browser B joins via the extracted join URL; B claims an
   item and A sees it within 2s; A finalizes; both see identical locked
   totals that sum to the bill.
2. **Manual-entry path**: unreadable fixture image -> Failed banner ->
   host adds 3 items by hand -> open -> claim -> finalize.

## Gates

Everything above runs in CI; merge is blocked on all of it except the e2e
job, which is required only on the milestone branches that touch it
([12-ci.md](12-ci.md)). Coverage thresholds: none - the invariant and
fixture tests are the meaningful bar, not a percentage.
