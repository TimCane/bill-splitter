// The only money formatter. Amounts are integer minor units on the wire; the
// server owns all math (docs/08-frontend-design.md, docs/09-ux-flows.md#copy-rules).
// The backend stores every amount as hundredths of the major unit regardless of
// currency (ReceiptParser.ToMinor multiplies by 100), so display divides by a
// fixed 100 and lets Intl render the currency's own symbol and digits.
const formatters = new Map<string, Intl.NumberFormat>()

function formatterFor(currency: string): Intl.NumberFormat {
  let formatter = formatters.get(currency)
  if (formatter === undefined) {
    formatter = new Intl.NumberFormat(undefined, { style: 'currency', currency })
    formatters.set(currency, formatter)
  }
  return formatter
}

export function formatMinor(amountMinor: number, currency: string): string {
  return formatterFor(currency).format(amountMinor / 100)
}
