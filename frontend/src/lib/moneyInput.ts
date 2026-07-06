// Parsing typed amounts into minor units for entry - the inverse of the display
// path in money.ts. This is input handling, not split math: the server still owns
// every total (CLAUDE.md, docs/02-domain-model.md#money).
export function currencyFractionDigits(currency: string): number {
  return (
    new Intl.NumberFormat(undefined, {
      style: 'currency',
      currency,
    }).resolvedOptions().maximumFractionDigits ?? 2
  )
}

/** Minor units -> an editable major-unit string, e.g. 1250 GBP -> "12.50". */
export function minorToInput(amountMinor: number, currency: string): string {
  const digits = currencyFractionDigits(currency)
  return (amountMinor / 10 ** digits).toFixed(digits)
}

/** A typed major-unit amount -> minor units, or null if it is not a valid amount. */
export function inputToMinor(value: string, currency: string): number | null {
  const trimmed = value.trim()
  if (trimmed === '') {
    return null
  }

  const amount = Number(trimmed)
  if (!Number.isFinite(amount) || amount < 0) {
    return null
  }

  return Math.round(amount * 10 ** currencyFractionDigits(currency))
}
