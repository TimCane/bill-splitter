# Receipt parsing reference

The detector/rule/score reference behind `ReceiptParser`. The pipeline decision
is [ADR-0006](adr/0006-receipt-parser-pipeline.md); the wire contract and
sidecar are [06-ocr-service.md](06-ocr-service.md); the corpus is the spec
([11-testing-strategy.md](11-testing-strategy.md#receiptparser)).

This is a reference, not a checklist. It captures the layouts and heuristics
worth supporting so a rule author has one place to look. Every entry is either
**current** (in code today) or **planned** (a capability in the tracking epic).
Nothing here is a contract on its own - the corpus is.

## Scope

Restaurant, bar and cafe bills only. A table splits a served meal: items, a
grand total, and some mix of tax, tip and service. The OCR sidecar ships
**English models only** ([ADR-0001](adr/0001-self-hosted-ocr.md)), which doubly
rules out the generic-parser territory below.

**Out of scope** (do not build from this doc):

- Weight-priced goods (`0.45kg Apples`), fuel litres, supermarket layouts.
- Multi-currency or locale-decimal expansion beyond what the corpus already
  contains. A few EUR/IVA restaurant receipts are in the corpus and stay green;
  that is preservation, not a target.
- Modelling discounts/negative lines in the domain - its own future decision
  (`Bill.DiscountMinor` plus Redis, DTO, docs/02-06). Today they are parked in
  `Warnings`.

## Pipeline

    OcrResult -> normalize -> classify lines -> item rules + engine
              -> bill detectors + engine -> validate -> ParsedReceipt

The engine runs every rule against a line and keeps the highest-confidence
candidate rather than the first regex to match. Adding a layout is a new rule,
not an edit to an existing one.

## Normalization

Runs before any rule sees a line.

- **Whitespace** collapse and trim. *(current: `Whitespace()`)*
- **Currency** guessed from the first `£`/`€`/`$` seen, else the session
  default. *(current: `GuessCurrency`)*
- **Decimal separator**: the money regex accepts `.` or `,` for the fraction.
  *(current)* Thousands grouping and locale forms (`1'234.50`, `1 234,50`) are
  out of scope.
- **OCR character misreads** *(planned)*: repair inside money/number spans only,
  never in item names.

| Misread | Fix | Example |
| --- | --- | --- |
| `O` | `0` | `1O.00` -> `10.00` |
| `S` | `5` | `12.SO` -> `12.50` |
| `l` `I` | `1` | `l2.00` -> `12.00` |
| `B` | `8` | `B.00` -> `8.00` |
| `E` | `£` | `E12.50` -> `£12.50` |

Guard: only digit-adjacent positions, so `7UP`, `No.8 Burger`, `Coke Zero`
survive intact.

## Price detection

A money token: 1-4 whole digits, `.` or `,`, two fraction digits, optional
leading currency symbol or minus, optional trailing single-letter VAT class.

- End-anchored money is the line gate. *(current: `MoneyAtEnd()` -
  `(?<neg>-)?(?<sym>[£€$])?\s*(?<whole>\d{1,4})[.,](?<frac>\d{2})(?:\s+[A-Z])?$`)*
- Trailing bare money for the unit-price column once the line total is removed.
  *(current: `TrailingMoney()`)*
- Negative/parenthesised amounts are discounts/refunds: parked in `Warnings`,
  unmodelled. *(current)*

## Quantity

- Leading count: `2 `, `2x `, `3 X `. *(current: `LeadingQuantity()`)*
- `Burger x2`, `Burger (2)` suffix forms. *(planned)*
- `Quantity` on `ParsedItem` is informational; the printed line total is the
  money that matters ([02-domain-model.md](02-domain-model.md#lineitem)).
- Do not treat a leading number as quantity when the name is a number
  (`7UP`, `No.8`).

## Unit price

- `@ 6.50` annotation alongside a line total: strip from the name. *(current:
  `AtUnitPrice()`)*
- Per-unit column: `2 ROAST BEEF 27.00` where a trailing number times the count
  equals the line total - drop the column. *(current: `StripUnitPrice`)*
- `2 @ $35.50` detail line printed under its item: the item already holds the
  line total; ignore the detail. *(current: `UnitPriceDetail()`)*

## Item line catalogue

Restaurant-relevant layouts. Letters follow the design notes; non-restaurant
ones (weight, PLU/barcode) are dropped.

| Layout | Example | Status |
| --- | --- | --- |
| name total | `Burger £5.00` | current |
| qty name total | `2 Burger £10.00` | current |
| qty name @unit total | `2 Burger @5.00 £10.00` | current |
| name dots total | `Burger........5.00` | current (leader strip) |
| name total taxcode | `Burger 5.00 A` | current |
| item #code | `Burger #12 5.00` | current (`ItemCode()`) |
| x-quantity | `Burger x2 £10.00` | planned |
| quantity suffix | `Burger (2) £10.00` | planned |
| name newline total | `Burger` / `£5.00` | planned (wrapped merge) |
| wrapped description | `Large Double` / `Bacon` / `£15.99` | planned (wrapped merge) |
| modifier lines | `Burger` / `+ Bacon` / `No Onion` | current (modifier attach) |

## Classify, don't itemise

Lines that are not items. *(current unless noted)*

- **Totals**: `Subtotal`, `Total`, `Grand Total`, `Amount Due`, `Balance`,
  `To Pay`. The grand total is the lowest `TOTAL` row by `Box.Y`; a same-row tie
  takes the larger amount. Intermediate/sub totals are dropped - we compute our
  own.
- **Tax**: `VAT`, `TAX`, `GST`, `IVA`. A VAT breakdown that pairs the word with
  a `%` rate is a summary, not payable tax, and is dropped; an inline-rate US
  sales-tax line is kept.
- **Tip**: `TIP`, `GRATUITY`.
- **Service**: `SERVICE` (incl. `Optional`/`Discretionary`). Also the split case
  where the label prints one line above its amount.
- **Payment noise**: `CASH`, `CARD`, `CHANGE`, `VISA`, `MASTERCARD`, `AUTH` -
  ignored, never items.
- **Category rollups**: `8 DRINK`, `3 FOOD`, bare `FOOD`; `6 Item(s)` counts -
  dropped.

Only rows above the grand total are classified as tax/tip/service or items;
everything at or below it (VAT breakdowns, payment lines, "divide by N" hints)
is trailing noise.

## Scoring

The engine picks the highest score, not the first match. Illustrative evidence
weights (calibrate against the corpus, do not hard-code blindly):

| Evidence | Score |
| --- | ---: |
| Ends with a valid price | +30 |
| Right-aligned price by `Box.X` | +25 |
| Starts with a quantity | +20 |
| Matches a known item pattern | +20 |
| Contains alphabetic text | +10 |
| Contains `@` unit price | +10 |
| Contains `TOTAL` | -100 |
| Contains a payment word | -100 |
| Contains `VAT`/tax | -80 |
| Very low OCR confidence | -20 |

Lines below `ConfidenceFloor` (0.5) are parked in `Warnings`, not itemised.
*(current)*

## Spatial information

OCR reorders and splits columns. Use `OcrBox`, do not trust line order alone.

- Sort by `Box.Y` then `Box.X` with a row tolerance before parsing. *(planned:
  box-sort; gated so already-ordered receipts are untouched. Seed fixture:
  `westminster-column-drift`.)*
- A price is almost always same-row, far-right - `Box.X` distinguishes a price
  column from a name.

## Validation

- **Reconciliation** *(planned)*: `sum(items) + tax + tip + service` vs the
  printed grand total, within tolerance. Mismatch -> a `Warnings` entry the host
  sees at review. Pure; no contract change.
- Claims reconcile exactly downstream in `SplitCalculator`
  ([ADR-0005](adr/0005-integer-shares-claims.md)); parser reconciliation is a
  soft warning, not a hard gate.

## Multi-line

- **Wrapped names** *(planned)*: join amount-less lines onto the next priced
  line until a price appears.
- **Modifiers** *(current)*: a pre-pass folds amount-less modifier lines into the
  priced line above them before candidates are built (`ModifierMerger`), so the
  item reads enriched - `Burger` + `+ Bacon` + `No Onion` -> `Burger + Bacon No
  Onion`, price unchanged. Only a leading `+`/`*` addition or a short
  `NO`/`EXTRA`/`ADD`/`HOLD`/`SUB`/`LESS`/`W/O`/`WITHOUT` form (one or two words,
  excluding payment/status words) attaches, and only directly below a priced
  line, so footers like `No payment received` are left alone.
- **Duplicate copies** *(planned)*: de-dupe repeated item blocks
  (merchant/customer/kitchen) without collapsing a genuinely repeated dish.

## Diagnostics

The engine records why a line was classified (rule, score, evidence) in-memory as
a `ParseDecision` per priced line so corpus tests can assert it *(current)*. The
bill stage traces the grand-total anchor, the tax/tip/service extras and the
dropped noise; the item stage traces the winning rule and its confidence for each
item row (keyword bill classification is priority-ordered, not scored, so those
decisions carry score `0`). It is a test-only surface - `ParseTraced` on the
internal engine returns it, `ReceiptParser.Parse` discards it. This never reaches
production logs - no receipt text, names or amounts are logged
([10-security-privacy.md](10-security-privacy.md)).

## Corpus discipline

Every misparse becomes a fixture before it is fixed: `ocr.json` (recorded
sidecar response) plus `expected.json`, run by `ReceiptParserCorpusTests`. A
capability lands with its own new fixtures; existing fixtures stay green, and an
`expected.json` changes only when a capability deliberately improves that
receipt, with justification.
