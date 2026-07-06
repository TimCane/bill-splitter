import { render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'

import { ReviewScreen } from '@/components/session/ReviewScreen'
import type { SessionSnapshot } from '@/lib/api/schemas'

function snapshot(): SessionSnapshot {
  return {
    sessionId: 's1',
    version: 3,
    state: 'Review',
    currency: 'GBP',
    expiresAt: '2026-07-06T20:00:00Z',
    shortCode: null,
    joinUrl: null,
    hostParticipantId: 'host',
    ocr: { status: 'Done', failureReason: null },
    participants: [{ participantId: 'host', displayName: 'Host', isHost: true }],
    items: [
      { itemId: 'i1', name: 'Margherita', quantity: 1, priceMinor: 1250, claims: [] },
      { itemId: 'i2', name: 'Peroni 660ml', quantity: 2, priceMinor: 1100, claims: [] },
    ],
    bill: {
      subtotalMinor: 2350,
      taxMinor: 0,
      tipMinor: 500,
      serviceMinor: 0,
      totalMinor: 5450,
      checksumMinor: 0,
    },
    unclaimedTotalMinor: 0,
    totals: [],
  }
}

function renderReview() {
  const client = new QueryClient()
  return render(
    <QueryClientProvider client={client}>
      <ReviewScreen snapshot={snapshot()} token="token" />
    </QueryClientProvider>,
  )
}

describe('ReviewScreen', () => {
  it('lists items with formatted prices and a quantity prefix', () => {
    renderReview()

    expect(screen.getByRole('heading', { name: /check the items/i })).toBeInTheDocument()
    expect(screen.getByText('Margherita')).toBeInTheDocument()
    expect(screen.getByText('£12.50')).toBeInTheDocument()
    expect(screen.getByText('2x')).toBeInTheDocument()
  })

  it('shows the printed total and the open action', () => {
    renderReview()

    expect(screen.getByText('Printed total')).toBeInTheDocument()
    expect(screen.getByText('£54.50')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /open the split/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /add item/i })).toBeInTheDocument()
  })
})
