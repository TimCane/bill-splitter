// Parsing typed amounts into minor units for entry - the inverse of the display
// path in money.ts. This is input handling, not split math: the server still owns
// every total (CLAUDE.md, docs/02-domain-model.md#money). The backend stores every
// amount as hundredths of the major unit, so entry scales by a fixed 100 - matching
// it per-currency would send 100x-wrong amounts for a zero-decimal currency.
const MINOR_PER_MAJOR = 100

/** Minor units -> an editable major-unit string, e.g. 1250 -> "12.50". */
export function minorToInput(amountMinor: number): string {
  return (amountMinor / MINOR_PER_MAJOR).toFixed(2)
}

/** A typed major-unit amount -> minor units, or null if it is not a valid amount. */
export function inputToMinor(value: string): number | null {
  const trimmed = value.trim()
  if (trimmed === '') {
    return null
  }

  const amount = Number(trimmed)
  if (!Number.isFinite(amount) || amount < 0) {
    return null
  }

  return Math.round(amount * MINOR_PER_MAJOR)
}
