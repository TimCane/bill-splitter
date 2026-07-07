# OCR service

`ocr/` is a deliberately dumb sidecar: image in, text lines with geometry
out. All receipt intelligence (parsing lines into items) lives in the
backend where it is strongly typed and unit-testable
([parsing](#parsing)). The sidecar has no state, no Redis, no MinIO.

## Stack

- Python 3.12, FastAPI + uvicorn
- PaddleOCR (PP-OCRv4), **English detection+recognition models only**
  ([ADR-0001](adr/0001-self-hosted-ocr.md)); models baked into the image at
  build time so cold start does not download anything
- CPU inference; one uvicorn worker; concurrency is throttled by the
  backend queue, not here
- Reachable only on the internal compose network - never exposed publicly

## Preprocessing

Real photos of thermal receipts are small, low-contrast and often rotated.
Before inference the sidecar runs a pure-Pillow pipeline
(`recognizer.preprocess_image`, numpy-free so it unit-tests without the
inference wheels):

1. **Grayscale** - colour carries no signal for printed text.
2. **Median denoise** (3x3) - removes the speckle of a phone photo.
3. **Autocontrast** - spreads a flat, greyed-out histogram.
4. **Upscale** so the shorter side reaches `OCR_UPSCALE_MIN_SIDE` px - never
   downscales a large, sharp photo.

Skew and rotation are left to PaddleOCR's **angle classifier**, not corrected
in pixels. Binarization is opt-in: it helps clean scans but erases faint print,
so it is off by default.

Flags (env, read per request):

| Flag | Default | Effect |
| --- | --- | --- |
| `OCR_PREPROCESS` | `true` | run the pipeline above |
| `OCR_ANGLE_CLS` | `true` | PaddleOCR angle classifier for rotated crops |
| `OCR_BINARIZE` | `false` | Otsu black-and-white (clean scans only) |
| `OCR_UPSCALE_MIN_SIDE` | `1000` | target shorter-side length in px |

The `/ocr` response shape is unchanged - preprocessing only affects the pixels
fed to inference.

## HTTP contract

### `POST /ocr`

Body: raw image bytes, `Content-Type: image/jpeg` or `image/png`, max
10MB. (Raw body, not multipart - one less parsing layer.)

- `200`

```json
{
  "durationMs": 3412,
  "lines": [
    {
      "text": "2 PERONI 660ML        11.00",
      "confidence": 0.94,
      "box": { "x": 42, "y": 310, "width": 828, "height": 38 }
    }
  ]
}
```

- `lines` ordered top-to-bottom by box `y`. `box` is the axis-aligned
  bounding rectangle of PaddleOCR's quad, in pixels of the submitted image.
- `422` undecodable image or header dimensions over 8000x8000 (checked
  before full decode - the sidecar's half of the decode-bomb guard,
  [10-security-privacy.md](10-security-privacy.md#upload-hardening)),
  `413` too large, `500` inference error (body is problem+json with a
  `detail`).

### `GET /healthz`

`200 {"status":"ok"}` once models are loaded (readiness, not just
liveness). The backend's `/healthz` probes this.

## Backend job flow

`OcrWorker` (a `BackgroundService` reading a bounded `Channel<OcrJob>`;
capacity and max-concurrency are the `Ocr__QueueCapacity` /
`Ocr__MaxConcurrency` config defaults, 16 / 2
- [07-backend-design.md](07-backend-design.md#infrastructure-project)):

1. CAS session `ocr.status -> Processing` (only while `state` is still
   `Processing`; otherwise abandon the job), broadcast `OcrStatusChanged` +
   `SnapshotUpdated`.
2. Fetch image from MinIO, `POST /ocr` (timeout 60s). Timeouts and HTTP
   error responses never retry - a second identical attempt will fail
   identically; only connection-level failures (refused, reset, DNS) retry
   once.
3. Parse lines -> items + bill (below).
4. CAS session, **only while `state` is still `Processing`**: items, bill,
   `state -> Review`, `ocr.status -> Done`; broadcast `OcrStatusChanged` +
   `SnapshotUpdated`. If the session already left `Processing` - the lazy
   recovery below fired first, or it was abandoned - the worker discards its
   parse result and writes nothing, so a late job never clobbers the host's
   manual entries.
5. Any failure: `state -> Review`, `ocr.status -> Failed`,
   `failureReason` set; broadcast as in step 4. The host enters items
   manually.
6. The channel is in-process, so a backend restart loses queued and
   in-flight jobs. Recovery is lazy, not a watchdog: any read of a session
   (snapshot `GET` or hub connect) still `Processing` more than 5 minutes
   after `createdAt` applies step 5 with `failureReason`
   `"OCR did not finish"`. A stuck spinner heals on the client's next
   reconnect or refresh; there is no dead end. Because every `OcrWorker` write
   is conditional on `state == Processing` (steps 1 and 4), this recovery and
   a slow-but-live worker cannot both land: whichever CAS-writes first wins
   and the other is a silent no-op.

## Parsing

`ReceiptParser` in `BillSplitter.Domain` - pure function
`Parse(OcrResult) -> ParsedReceipt { Items, Bill, Warnings }`. Fixture-driven
tests are the spec ([11-testing-strategy.md](11-testing-strategy.md#receiptparser));
the heuristics below are the initial implementation, expected to grow with
the fixture corpus. The detector/rule/score reference and the target pipeline
shape are [15-receipt-parsing.md](15-receipt-parsing.md)
([ADR-0006](adr/0006-receipt-parser-pipeline.md)).

`Parse` is a thin static facade over an internal engine under
`BillSplitter.Domain/Parsing` (`Models`, `Engine`, and - as each concern is
extracted - `Normalization`, `Classification`, `Rules`, `Validation`). The
pipeline is `normalize -> classify -> item rules + engine -> bill detectors +
engine -> validate`; Phase A moves the heuristics below out of the engine one
layer at a time, corpus green at every step, without changing `Parse`'s
signature or output. The facade keeps the single call site (`OcrWorker`) and
the no-DI, pure-Domain status quo unchanged.

Before candidates are read, two gated pre-passes run in `Parsing/Multiline`, name
assembly first, then modifier folding. `WrappedNameMerger` folds an item name
wrapped across lines onto its price row (`Classic` / `BAO` / `£6.50` -> one
`Classic BAO £6.50`); it fires only for a nameless priced line on a receipt that
has a structural total, borrowing a bounded run of letter-only fragments that sit
above the price and in its left column (by `Box.Y`/`Box.X`, not list order) - so a
centred store header, an already-inline receipt, a non-receipt, or a fully
column-drifted layout is untouched. `ModifierMerger` then folds amount-less
modifier lines (`+ Bacon`, `No Onion`, `Extra Sauce`) directly below a priced line
into that line's name ahead of the money token, so the item reads enriched
(`Burger + Bacon No Onion`) and no stray row is emitted; it is conservative by
design - only leading `+`/`*` additions or a short keyword form attach, leaving
footers such as `No payment received` untouched. Modifiers fold after name
assembly, so they attach to whole priced rows rather than a still-nameless price.

Every line is then run through `ITextNormalizer` (`Parsing/Normalization`); the
default `BasicNormalizer` trims surrounding whitespace and collapses internal
runs to single spaces. This is generic line tidying only - item-name concerns
(`#code`/`@unit` stripping) live in the item rules, not here.

A second `Parsing/Normalization` pass, `MoneyMisreadRepair`, then fixes the OCR
letter/digit confusions that only occur in a price (`O`/`0`, `S`->5, `l`/`I`->1,
`B`->8, a leading `E`->£), so `E12.5O` reaches the money regex as `£12.50`. It is
gated to a trailing money-shaped token that carries at least one real digit and
one repairable glyph, so alphabetic item names (`7UP`, `No.8 Burger`, `Coke
Zero`, `KX BOB`) are never touched.

Once the priced candidates are built, `BoxOrderer` (`Parsing/Spatial`) restores
reading order by sorting them on `Box.Y` then `Box.X`, so a sidecar that emitted
rows scrambled still yields items top-to-bottom and a correctly anchored grand
total. It is gated - an already-ordered receipt is returned untouched - and
reorders the priced candidates only, so a label captured above its amount is not
disturbed. A name and price split into separate columns and drifted to
non-adjacent rows is not reunified - the sort reorders items already read, it
never invents one.

Each priced line is then mapped to a `LineType` by `ILineClassifier`
(`Parsing/Classification`); the default `KeywordClassifier` owns the keyword and
positional decisions of step 3 (subtotal, item-count, rollup, VAT breakdown,
tax, tip, service, total, payment noise, bare charge). Highest-priority match
wins, so a "Total Taxes" row reads as tax before total and a "20% VAT" breakdown
is dropped before it is counted as payable tax.

The bill fields are then read off the priced candidates by the bill detectors
(`Parsing/Detectors`). `GrandTotalDetector` anchors on the grand total using the
classifier's `IsGrandTotalCandidate` - a looser test than `Classify`, since a
"Total incl. VAT" row is the amount due even though its tax word outranks the
total word in the `LineType` precedence - taking the lowest such row by `Box.Y`
and, on a same-row tie, the larger amount. `BillDetectionEngine` then classifies
only the rows above that anchor: `Tax`/`Tip`/`Service` become `bill.taxMinor`/
`tipMinor`/`serviceMinor`; subtotals, rollups, VAT breakdowns, intermediate
totals and payment noise are dropped; a bare charge is parked as a warning; and
whatever is left is handed back as the item rows. Everything at or below the
grand total is trailing noise the receipt prints after the amount due. It
returns a `BillDetection` (the `Bill`, the item rows, and its warnings); the
engine forwards the item rows to the item rules and merges the warnings.

Each item row above the total is then read by the item rules (`Parsing/Rules`).
Every `IReceiptRule` inspects the row and returns an `ItemCandidate` - a parsed
item or a reject, with the confidence the `ItemSelectionEngine` ranks it by - or
`null` when the row does not fit its layout. The engine keeps the
highest-confidence candidate rather than the first rule to match; a reject that
wins parks a warning instead of adding an item. The default set is
`NamelessPriceRejectRule` (a price whose name shapes away to nothing) over
`UnitPriceColumnRule` (a reconciling per-unit column to drop) over
`QuantityNamePriceRule` (the everyday name reading, always applies). Confidences
are calibrated so selection reproduces the old first-match order; the shared
name-shaping primitives (clean, quantity, unit-price column) live in `ItemText`.

As it runs, the pipeline records a `ParseDecision` per priced line - the winning
rule, its score and the evidence - into an in-memory trace. The bill stage traces
the grand-total anchor, the tax/tip/service extras and the dropped noise; the item
stage traces which rule won each item row (score = the item engine's confidence;
keyword bill classification is priority-ordered, not scored, so those carry `0`).
The trace is a **test-only** diagnostic: `ParseTraced` on the internal engine
returns it for the corpus tests, `ReceiptParser.Parse` discards it, and it never
rides the `ParsedReceipt` wire contract or reaches a log - no receipt text leaves
the process ([10-security-privacy.md](10-security-privacy.md),
[15-receipt-parsing.md](15-receipt-parsing.md#diagnostics)).

1. **Price extraction.** A line is a candidate item/amount row if it ends
   with a money token: `(\d{1,4})[.,](\d{2})` optionally preceded by a
   currency symbol. Amount = digits as minor units. Reject rows whose money
   token is immediately followed by more text (e.g. `11.00%`).
2. **Grand total first.** The grand total is the **lowest** `TOTAL|AMOUNT
   DUE|BALANCE DUE|TO PAY` row on the receipt (not `SUBTOTAL`, not an
   `N Item(s)` count); a same-height tie takes the larger amount. Everything
   **at or below** it - VAT breakdowns, payment lines, "divide by N" hints -
   is trailing noise and is dropped, so a `VAT: 0.67` line printed under the
   total never lands in `bill.taxMinor`.
3. **Keyword rows** above the total (case-insensitive):
   - `SUBTOTAL|SUB TOTAL`, `N Item(s)`, intermediate `TOTAL` rows -> ignore
     (we compute our own)
   - `TAX|VAT|GST` -> `bill.taxMinor`
   - `TIP|GRATUITY` -> `bill.tipMinor`
   - `SERVICE` -> `bill.serviceMinor`
   - `CASH|CHANGE|CARD|VISA|MASTERCARD|AUTH` -> ignore (payment noise)
4. **Item rows**: any remaining candidate row above the total row. Name =
   text before the money token, trimmed of dot leaders, `#` codes and `@ 6.50`
   unit-price annotations. Quantity: leading `(\d{1,2})\s?[xX]?\s` ->
   `quantity`, stripped from the name; `priceMinor` is always the row's printed
   amount (already the line total on virtually all receipts).
5. **Discards** produce `Warnings` (shown on the review screen so the host
   knows what to double-check): rows with a price but no name, negative
   amounts (discount rows - parked as a warning in MVP, not modeled),
   confidence < 0.5.
6. **Currency guess**: first currency symbol seen (`£ -> GBP`, `€ -> EUR`,
   `$ -> USD`); default `GBP`. Host confirms at review.

Every rule above is deterministic on the sidecar's JSON - fixtures are
recorded sidecar responses, so parser tests run without Python or images.

## Compose service

Added to `docker-compose.yml` **together with** the `ocr/` scaffold
(milestone 1, [14-build-order.md](14-build-order.md)):

```yaml
  # PaddleOCR sidecar - internal only, no published ports.
  ocr:
    build:
      context: ../ocr
    restart: unless-stopped
    networks:
      - backend
```

Image size warning for the handoff developer: PaddlePaddle CPU wheels are
~600MB; expect a 1.5-2GB image and a multi-minute first build. The inference
wheels are pinned in `requirements-ocr.txt` and installed into the image only;
`requirements.txt` holds just the light web-serving deps so CI can run the stub
tests without pulling PaddlePaddle
([11-testing-strategy.md](11-testing-strategy.md)).
