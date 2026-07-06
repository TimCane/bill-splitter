import type { ReactNode } from 'react'
import { CircleAlert, CircleCheck } from 'lucide-react'

import type { Bill, Ocr } from '@/lib/api/schemas'
import { cn } from '@/lib/utils'
import { formatMinor } from '@/lib/money'

type Props = {
  bill: Bill
  ocr: Ocr
  currency: string
}

// Advisory, never blocking (docs/02-domain-model.md#checksum). Copy is verbatim from
// docs/09-ux-flows.md#4-review-host-gate---state-review; amounts go through formatMinor.
export function ChecksumBanner({ bill, ocr, currency }: Props) {
  if (ocr.status === 'Failed') {
    return (
      <Banner tone="amber">
        Couldn&apos;t read the receipt. Add items by hand, or start over with a
        better photo.
      </Banner>
    )
  }

  if (bill.checksumMinor === 0) {
    return (
      <Banner tone="green">
        Items + extras match the total. {formatMinor(bill.totalMinor, currency)}
      </Banner>
    )
  }

  const delta = formatMinor(Math.abs(bill.checksumMinor), currency)
  const direction = bill.checksumMinor > 0 ? 'over' : 'under'
  return (
    <Banner tone="amber">
      Items + extras are {delta} {direction} the printed total. Fix items or
      edit the total.
    </Banner>
  )
}

function Banner({
  tone,
  children,
}: {
  tone: 'green' | 'amber'
  children: ReactNode
}) {
  return (
    <div
      role="status"
      className={cn(
        'flex items-start gap-2 rounded-lg border p-3 text-sm',
        tone === 'green'
          ? 'border-emerald-300 bg-emerald-50 text-emerald-900 dark:border-emerald-900 dark:bg-emerald-950 dark:text-emerald-100'
          : 'border-amber-300 bg-amber-50 text-amber-900 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100',
      )}
    >
      {tone === 'green' ? (
        <CircleCheck className="mt-0.5 size-4 shrink-0" />
      ) : (
        <CircleAlert className="mt-0.5 size-4 shrink-0" />
      )}
      <p>{children}</p>
    </div>
  )
}
