# Domain model

The C# types live in `BillSplitter.Domain`. The session aggregate is one
document; there are no cross-aggregate references
([03-redis-schema.md](03-redis-schema.md)).

## Money

- Every amount is an **integer in minor units** (`long`, e.g. pence/cents),
  everywhere: domain, Redis, DTOs, the wire. No `decimal`, no `double`,
  ever.
- One **currency per session** (ISO 4217 code, e.g. `"GBP"`). Set during
  review (OCR guess, host-correctable), display-only after that.
- All division uses **largest-remainder distribution** (below) so integer
  allocations always sum exactly to the amount being divided.

### Largest-remainder distribution

`Distribute(long amount, IReadOnlyList<long> weights) -> long[]`

1. `share_i = amount * weight_i / totalWeight` (integer floor division).
2. `remainder = amount - sum(share_i)` (always `0 <= remainder < n`).
3. Give one extra minor unit to the `remainder` recipients with the largest
   fractional parts `(amount * weight_i) % totalWeight`; break ties by list
   index (lowest index wins).

Deterministic, exact, and order-stable: callers must pass weights in a
stable order (participants by `joinedAt`, then `id`).

## Entities

### Session (aggregate root)

| Field | Type | Notes |
| --- | --- | --- |
| `Id` | `string` | 22-char base64url of 16 random bytes |
| `Version` | `int` | optimistic concurrency counter, +1 per write |
| `State` | `SessionState` | `Processing`, `Review`, `Open`, `Finalized` |
| `Currency` | `string` | ISO 4217; default `"GBP"` until review sets it |
| `ShortCode` | `string?` | 6 chars, minted at open; alphabet `ABCDEFGHJKMNPQRSTUVWXYZ23456789` (no 0/O/1/I/L) |
| `CreatedAt` | `DateTimeOffset` | UTC |
| `FinalizedAt` | `DateTimeOffset?` | UTC |
| `HostParticipantId` | `string` | the creator |
| `Participants` | `List<Participant>` | max 20 |
| `Items` | `List<LineItem>` | max 100 |
| `Bill` | `Bill` | extras + printed total |
| `Ocr` | `OcrInfo` | status + failure reason |

### Participant

| Field | Type | Notes |
| --- | --- | --- |
| `Id` | `string` | 22-char base64url |
| `TokenHash` | `string` | hex SHA-256 of the participant token; the raw token is returned once at create/join and never stored |
| `DisplayName` | `string` | 1-30 chars, trimmed; collisions allowed |
| `IsHost` | `bool` | exactly one per session |
| `JoinedAt` | `DateTimeOffset` | UTC; stable ordering key |

### LineItem

| Field | Type | Notes |
| --- | --- | --- |
| `Id` | `string` | 22-char base64url |
| `Name` | `string` | 1-80 chars |
| `Quantity` | `int` | informational only (display "x3"); >= 1 |
| `PriceMinor` | `long` | total price of the whole line, not per unit; >= 0 |
| `Claims` | `List<Claim>` | empty = unclaimed |

`Quantity` never affects math: `PriceMinor` is the full line amount and
shares divide it. "I had 2 of the 3 beers" is `SetShares(itemId, 2)`.

### Claim

| Field | Type | Notes |
| --- | --- | --- |
| `ParticipantId` | `string` | one claim per participant per item |
| `Shares` | `int` | 1-99; default 1 |

### Bill

| Field | Type | Notes |
| --- | --- | --- |
| `TaxMinor` | `long` | >= 0 |
| `TipMinor` | `long` | >= 0 |
| `ServiceMinor` | `long` | >= 0 |
| `TotalMinor` | `long` | the printed bill total; >= 0 |

`SubtotalMinor` is always computed (`sum(Items.PriceMinor)`), never stored.

### OcrInfo

| Field | Type | Notes |
| --- | --- | --- |
| `Status` | `OcrStatus` | `Pending`, `Processing`, `Done`, `Failed` |
| `FailureReason` | `string?` | human-readable, shown to host only |

## Checksum

`Checksum = SubtotalMinor + TaxMinor + TipMinor + ServiceMinor - TotalMinor`

- `0` -> bill reconciles; review screen shows a green banner.
- Non-zero -> amber banner with the signed difference. The host may open
  the session anyway (OCR of the total may be the wrong part); the checksum
  is advisory, never blocking.

## Split math (the core algorithm)

Implemented in `SplitCalculator` (pure, stateless, exhaustively unit-tested
- see [11-testing-strategy.md](11-testing-strategy.md)). Recomputed from
scratch on every snapshot build; nothing is cached.

For each participant `p`, in `Open` state:

1. **Items**: for each item with claims, distribute `PriceMinor` across its
   claimants using largest-remainder with `Shares` as weights (claimants
   ordered by `JoinedAt`, then `Id`). `p.ItemsMinor` = sum of p's
   allocations.
2. **Extras** (tax, tip, service - each distributed independently):
   distribute across participants with `ItemsMinor` as weights. If everyone's
   `ItemsMinor` is 0 (nothing claimed yet), extras stay unallocated and each
   `p.TaxMinor` etc. is 0.
3. **Unclaimed**: `UnclaimedMinor` = 0 for everyone while `Open`. The
   session-level `UnclaimedTotalMinor` (sum of items with no claims) is
   shown to the whole table.
4. `p.TotalMinor = ItemsMinor + TaxMinor + TipMinor + ServiceMinor +
   UnclaimedMinor`.

At **finalize**, additionally:

5. Distribute `UnclaimedTotalMinor` equally across all participants
   (weights all 1, ordered by `JoinedAt`, then `Id`) into
   `p.UnclaimedMinor`. Extras weights remain claimed `ItemsMinor` only -
   unclaimed allocations do not attract a share of tax/tip. Deliberate:
   unclaimed items were nobody's order, so their share carries no tip
   judgment.
6. If nothing was claimed at all, extras distribute equally (weights all 1).

### Invariants (property-test these)

- `sum(p.ItemsMinor) == sum(claimed items' PriceMinor)`
- After finalize: `sum(p.TotalMinor) == SubtotalMinor + TaxMinor + TipMinor
  + ServiceMinor` (i.e. the whole bill is paid, regardless of `TotalMinor`
  checksum state)
- Removing a claim and re-adding it yields identical allocations
  (determinism)
- No allocation is negative

## Domain rules (enforced in the aggregate, surfaced as 409/422)

| Rule | Where |
| --- | --- |
| Item CRUD and bill edits only in `Review` | `Session.EnsureState` |
| Claims only in `Open` | `Session.EnsureState` |
| Join only in `Open`; max 20 participants | `Session.Join` |
| `Shares` 1-99; claim references an existing item | `Session.SetShares` |
| Finalize only in `Open`, only by host | `Session.Finalize` |
| Open only in `Review`, only by host | `Session.Open` |
| Max 100 items; name/price bounds | `Session.AddItem` / `UpdateItem` |
