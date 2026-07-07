# ADR-0006: Rule/engine parser pipeline

Status: accepted, 2026-07-07

## Context

`ReceiptParser` began as one static class: a single money-at-end regex gate,
keyword classification, and positional total detection, all inside one `Parse`
method. It works - a 33-fixture corpus is green - but every new receipt quirk
wedges another branch into the same method, and the branches interact. Adding a
layout means re-reading the whole method and hoping the change does not perturb
an unrelated fixture.

The design notes we collected (distilled into
[15-receipt-parsing.md](../15-receipt-parsing.md)) argue for the shape mature
receipt parsers converge on: many small detectors, each scoring a line, highest
confidence wins - not one growing regex.

We are restaurant-scoped. Weight-priced goods, fuel, and supermarket layouts are
out; that prunes most of the generic-parser complexity from the notes.

## Decision

Decompose `ReceiptParser` into a pipeline under `BillSplitter.Domain/Parsing`:

    OcrResult -> normalize -> classify lines -> item rules + engine
              -> bill detectors + engine -> validate -> ParsedReceipt

- Each parsing concern is a small named unit (`ITextNormalizer`,
  `ILineClassifier`, `IReceiptRule`, bill detectors, validators) instead of a
  branch in one method.
- The engine selects the highest-confidence candidate. Rules carry a score;
  adding a layout is a new rule, not an edit to existing ones.
- `ReceiptParser.Parse(OcrResult)` stays a static facade over the engine, so the
  one call site (`OcrWorker.cs`) and the pure-Domain, no-DI status quo are
  unchanged.
- Migration is behaviour-preserving first (the corpus is the contract, green at
  every step), then capabilities land one at a time, each proven by new
  fixtures. Sequencing and the sub-issue breakdown live in the tracking epic.

## Consequences

- Adding a receipt layout is additive: implement a rule, add a fixture. Existing
  rules and their fixtures are untouched.
- The corpus stays the real spec; the pipeline does not change what "correct"
  means, only how it is computed ([11-testing-strategy.md](../11-testing-strategy.md#receiptparser)).
- More types and files than one class. Justified once rules number in the tens;
  the notes expect 20-30.
- A scored engine can diverge from today's first-match order if confidences are
  miscalibrated. Phase-one rules are tuned to reproduce current output exactly;
  the green corpus catches drift.
- Diagnostics (why a rule won) are recorded in-memory for tests only. Nothing
  about receipt contents is logged in production
  ([10-security-privacy.md](../10-security-privacy.md)).
- Out of scope, deferred to their own decisions: modelling discounts in the
  domain (touches `Bill`, Redis, DTOs); non-restaurant layouts; multi-currency
  and locale-decimal expansion beyond the current corpus.
