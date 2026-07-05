# ADR-0005: Shared claims with integer shares, minor-unit money

Status: accepted, 2026-07-04

## Context

The claim model is the core data-model decision. Exclusive claims fail on
any shared dish; percentage splits are fiddly phone UI and invite
doesn't-sum-to-100 states. Money in floats or JSON-crossing decimals
invites penny drift.

## Decision

Any number of participants can claim a line item; each claim carries an
integer `shares` weight (default 1, "I had 2 of the 3 beers" = 2). All
amounts are integer minor units end to end. Every division - item across
claimants, extras across participants, unclaimed across everyone at
finalize - uses largest-remainder distribution with deterministic
tie-breaking, so integer allocations always sum exactly. Tax/tip/service
allocate pro-rata to claimed subtotals; unclaimed items split equally at
finalize and attract no extras share.

## Consequences

- Every displayed total is exact; the whole bill is always fully covered
  after finalize (a property test guards this).
- Totals change retroactively when a claimant joins a shared item - correct
  but surprising; the UI must animate the change.
- All math lives server-side in one pure `SplitCalculator`; clients only
  format. Percentage UI can layer on later by mapping to shares.
