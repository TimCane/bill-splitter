import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'

import { SummaryScreen } from '@/components/session/SummaryScreen'
import type { SessionSnapshot } from '@/lib/api/schemas'

function snapshot(): SessionSnapshot {
  return {
    sessionId: 's1',
    version: 9,
    state: 'Finalized',
    currency: 'GBP',
    expiresAt: '2026-07-06T20:00:00Z',
    shortCode: 'K7MPQ2',
    joinUrl: 'https://split.example.com/s/s1',
    hostParticipantId: 'tim',
    ocr: { status: 'Done', failureReason: null, warnings: [] },
    participants: [
      { participantId: 'tim', displayName: 'Tim', isHost: true },
      { participantId: 'sam', displayName: 'Sam', isHost: false },
    ],
    items: [
      {
        itemId: 'i1',
        name: 'Margherita',
        quantity: 1,
        priceMinor: 1250,
        claims: [{ participantId: 'tim', shares: 1, allocatedMinor: 1250 }],
      },
    ],
    bill: {
      subtotalMinor: 1250,
      taxMinor: 0,
      tipMinor: 418,
      serviceMinor: 0,
      totalMinor: 1668,
      checksumMinor: 0,
    },
    unclaimedTotalMinor: 650,
    totals: [
      {
        participantId: 'tim',
        itemsMinor: 1250,
        taxMinor: 0,
        tipMinor: 209,
        serviceMinor: 0,
        unclaimedMinor: 163,
        totalMinor: 1622,
      },
      {
        participantId: 'sam',
        itemsMinor: 0,
        taxMinor: 0,
        tipMinor: 209,
        serviceMinor: 0,
        unclaimedMinor: 163,
        totalMinor: 372,
      },
    ],
  }
}

describe('SummaryScreen', () => {
  it('shows the locked header, the grand total and per-person totals', () => {
    render(<SummaryScreen snapshot={snapshot()} />)

    expect(
      screen.getByRole('heading', { name: /split locked/i }),
    ).toBeInTheDocument()
    // 1622 + 372 = 1994, summed from the server allocations.
    expect(screen.getByText('£19.94')).toBeInTheDocument()
    expect(screen.getByText('£16.22')).toBeInTheDocument()
    expect(
      screen.getByText(/unclaimed £6\.50 was split between 2 people/i),
    ).toBeInTheDocument()
  })

  it('expands a person to reveal their item and extras breakdown', async () => {
    const user = userEvent.setup()
    render(<SummaryScreen snapshot={snapshot()} />)

    await user.click(screen.getByRole('button', { name: /tim/i }))

    expect(screen.getByText('Margherita')).toBeInTheDocument()
    expect(screen.getByText('+ tip')).toBeInTheDocument()
    expect(screen.getByText('+ unclaimed')).toBeInTheDocument()
  })

  it('counts down to the server-provided expiry', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-07-06T19:02:00Z'))
    try {
      render(<SummaryScreen snapshot={snapshot()} />)
      expect(screen.getByText(/gone in 58 min/i)).toBeInTheDocument()
    } finally {
      vi.useRealTimers()
    }
  })
})
