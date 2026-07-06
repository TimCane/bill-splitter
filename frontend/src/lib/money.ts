// The only money formatter. Amounts are integer minor units on the wire; the
// server owns all math (docs/08-frontend-design.md, docs/09-ux-flows.md#copy-rules).
// Nothing here computes - it divides by the currency's fraction scale and formats.
export function formatMinor(amountMinor: number, currency: string): string {
  const formatter = new Intl.NumberFormat(undefined, {
    style: 'currency',
    currency,
  })
  const fractionDigits = formatter.resolvedOptions().maximumFractionDigits ?? 2
  return formatter.format(amountMinor / 10 ** fractionDigits)
}
