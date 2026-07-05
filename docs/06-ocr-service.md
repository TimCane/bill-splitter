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

`OcrWorker` (a `BackgroundService` reading a bounded
`Channel<OcrJob>(capacity: 16)`, max 2 concurrent jobs
- [07-backend-design.md](07-backend-design.md#background-processing)):

1. CAS session `ocr.status -> Processing`, broadcast `OcrStatusChanged` +
   `SnapshotUpdated`.
2. Fetch image from MinIO, `POST /ocr` (timeout 60s). Timeouts and HTTP
   error responses never retry - a second identical attempt will fail
   identically; only connection-level failures (refused, reset, DNS) retry
   once.
3. Parse lines -> items + bill (below).
4. CAS session: items, bill, `state -> Review`, `ocr.status -> Done`;
   broadcast `OcrStatusChanged` + `SnapshotUpdated`.
5. Any failure: `state -> Review`, `ocr.status -> Failed`,
   `failureReason` set; broadcast as in step 4. The host enters items
   manually.
6. The channel is in-process, so a backend restart loses queued and
   in-flight jobs. Recovery is lazy, not a watchdog: any read of a session
   (snapshot `GET` or hub connect) still `Processing` more than 5 minutes
   after `createdAt` applies step 5 with `failureReason`
   `"OCR did not finish"`. A stuck spinner heals on the client's next
   reconnect or refresh; there is no dead end.

## Parsing

`ReceiptParser` in `BillSplitter.Domain` - pure function
`Parse(OcrResult) -> ParsedReceipt { Items, Bill, Warnings }`. Fixture-driven
tests are the spec ([11-testing-strategy.md](11-testing-strategy.md#receiptparser));
the heuristics below are the initial implementation, expected to grow with
the fixture corpus.

1. **Price extraction.** A line is a candidate item/amount row if it ends
   with a money token: `(\d{1,4})[.,](\d{2})` optionally preceded by a
   currency symbol. Amount = digits as minor units. Reject rows whose money
   token is immediately followed by more text (e.g. `11.00%`).
2. **Keyword rows** (case-insensitive, checked before item classification):
   - `SUBTOTAL|SUB TOTAL` -> ignore (we compute our own)
   - `TAX|VAT|GST` -> `bill.taxMinor`
   - `TIP|GRATUITY` -> `bill.tipMinor`
   - `SERVICE` -> `bill.serviceMinor`
   - `TOTAL|AMOUNT DUE|BALANCE DUE|TO PAY` (and not `SUBTOTAL`) ->
     `bill.totalMinor`; if several match, the **lowest on the receipt**
     wins (grand totals print last); same-height ties take the larger
     amount
   - `CASH|CHANGE|CARD|VISA|MASTERCARD|AUTH` -> ignore (payment noise)
3. **Item rows**: any remaining candidate row above the total row. Name =
   text before the money token, trimmed of dot leaders and `#` codes.
   Quantity: leading `(\d{1,2})\s?[xX]?\s` -> `quantity`, stripped from the
   name; `priceMinor` is always the row's printed amount (already the line
   total on virtually all receipts).
4. **Discards** produce `Warnings` (shown on the review screen so the host
   knows what to double-check): rows with a price but no name, negative
   amounts (discount rows - parked as a warning in MVP, not modeled),
   confidence < 0.5.
5. **Currency guess**: first currency symbol seen (`£ -> GBP`, `€ -> EUR`,
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
~600MB; expect a 1.5-2GB image and a multi-minute first build. Pin wheel
versions in `requirements.txt`.
