import { render, screen } from '@testing-library/react'

import { ChecksumBanner } from '@/components/session/ChecksumBanner'
import type { Bill, Ocr } from '@/lib/api/schemas'

const doneOcr: Ocr = { status: 'Done', failureReason: null, warnings: [] }

function bill(overrides: Partial<Bill>): Bill {
  return {
    subtotalMinor: 0,
    taxMinor: 0,
    tipMinor: 0,
    serviceMinor: 0,
    totalMinor: 0,
    checksumMinor: 0,
    ...overrides,
  }
}

describe('ChecksumBanner', () => {
  it('reads green with the total when the checksum is zero', () => {
    render(
      <ChecksumBanner
        bill={bill({ totalMinor: 5450 })}
        ocr={doneOcr}
        currency="GBP"
      />,
    )

    expect(
      screen.getByText('Items + extras match the total. £54.50'),
    ).toBeInTheDocument()
  })

  it('reads amber over when items + extras exceed the total', () => {
    render(
      <ChecksumBanner
        bill={bill({ checksumMinor: 250 })}
        ocr={doneOcr}
        currency="GBP"
      />,
    )

    expect(
      screen.getByText(
        'Items + extras are £2.50 over the printed total. Fix items or edit the total.',
      ),
    ).toBeInTheDocument()
  })

  it('reads amber under when items + extras fall short', () => {
    render(
      <ChecksumBanner
        bill={bill({ checksumMinor: -100 })}
        ocr={doneOcr}
        currency="GBP"
      />,
    )

    expect(
      screen.getByText(
        'Items + extras are £1.00 under the printed total. Fix items or edit the total.',
      ),
    ).toBeInTheDocument()
  })

  it('reads the OCR-failed variant regardless of the checksum', () => {
    render(
      <ChecksumBanner
        bill={bill({ checksumMinor: 999 })}
        ocr={{ status: 'Failed', failureReason: 'OCR timed out', warnings: [] }}
        currency="GBP"
      />,
    )

    expect(
      screen.getByText(
        "Couldn't read the receipt. Add items by hand, or start over with a better photo.",
      ),
    ).toBeInTheDocument()
  })
})
