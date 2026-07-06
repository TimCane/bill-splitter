import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { vi } from 'vitest'

import { storeIdentity } from '@/hooks/useParticipantToken'
import { getSession } from '@/lib/api/client'
import type { SessionSnapshot } from '@/lib/api/schemas'
import { Session } from '@/routes/Session'

// The hub is mocked at the contract.ts boundary
// (docs/11-testing-strategy.md#frontend-vitest--rtl): gestures are spies and
// inbound snapshots are pushed through the captured onSnapshot handler, so the
// real useSessionHub/useSession wiring - version guard included - is under test.
const {
  fakeConnection,
  snapshotHandlers,
  claimItemMock,
  unclaimItemMock,
  setSharesMock,
} = vi.hoisted(() => ({
  fakeConnection: {
    start: () => Promise.resolve(),
    stop: () => Promise.resolve(),
    onreconnecting: () => {},
    onreconnected: () => {},
    onclose: () => {},
  },
  snapshotHandlers: {} as Record<string, (snapshot: unknown) => void>,
  claimItemMock: vi.fn(() => Promise.resolve()),
  unclaimItemMock: vi.fn(() => Promise.resolve()),
  setSharesMock: vi.fn(() => Promise.resolve()),
}))

vi.mock('react-router', async (importOriginal) => ({
  ...(await importOriginal<typeof import('react-router')>()),
  useParams: () => ({ sessionId: 's1' }),
}))

vi.mock('@/lib/realtime/connection', () => ({
  createConnection: () => fakeConnection,
}))

vi.mock('@/lib/realtime/contract', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/realtime/contract')>()),
  claimItem: claimItemMock,
  unclaimItem: unclaimItemMock,
  setShares: setSharesMock,
  onSnapshot: (
    _connection: unknown,
    event: string,
    handler: (snapshot: unknown) => void,
  ) => {
    snapshotHandlers[event] = handler
  },
  onOcrStatusChanged: () => {},
}))

vi.mock('@/lib/api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/api/client')>()),
  getSession: vi.fn(),
}))

function snapshot(
  version: number,
  claims: { participantId: string; shares: number; allocatedMinor: number }[],
): SessionSnapshot {
  return {
    sessionId: 's1',
    version,
    state: 'Open',
    currency: 'GBP',
    expiresAt: '2026-07-06T20:00:00Z',
    shortCode: 'K7MPQ2',
    joinUrl: 'https://split.example.com/s/s1',
    hostParticipantId: 'host',
    ocr: { status: 'Done', failureReason: null, warnings: [] },
    participants: [
      { participantId: 'host', displayName: 'Host', isHost: true },
      { participantId: 'me', displayName: 'Tim', isHost: false },
    ],
    items: [
      {
        itemId: 'i1',
        name: 'Margherita',
        quantity: 1,
        priceMinor: 1250,
        claims,
      },
    ],
    bill: {
      subtotalMinor: 1250,
      taxMinor: 0,
      tipMinor: 0,
      serviceMinor: 0,
      totalMinor: 1250,
      checksumMinor: 0,
    },
    unclaimedTotalMinor: 1250,
    totals: [],
  }
}

function pushSnapshot(next: SessionSnapshot) {
  const handler = snapshotHandlers.SnapshotUpdated
  if (!handler) {
    throw new Error('SnapshotUpdated handler was never registered')
  }

  act(() => {
    handler(next)
  })
}

function renderClaimScreen() {
  return render(
    <QueryClientProvider client={new QueryClient()}>
      <Session />
    </QueryClientProvider>,
  )
}

describe('claim interactions', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    storeIdentity(
      's1',
      { participantId: 'me', participantToken: 'token' },
      Date.now(),
    )
    vi.mocked(getSession).mockResolvedValue(snapshot(1, []))
  })

  it('a claim-row tap calls the hub claim method', async () => {
    const user = userEvent.setup()
    renderClaimScreen()

    await user.click(await screen.findByRole('button', { name: 'Mine' }))

    expect(claimItemMock).toHaveBeenCalledWith(expect.anything(), 'i1')
  })

  it('a tap on a claimed row unclaims it', async () => {
    const user = userEvent.setup()
    vi.mocked(getSession).mockResolvedValue(
      snapshot(1, [{ participantId: 'me', shares: 1, allocatedMinor: 1250 }]),
    )
    renderClaimScreen()

    await user.click(await screen.findByRole('button', { name: 'Claimed' }))

    expect(unclaimItemMock).toHaveBeenCalledWith(expect.anything(), 'i1')
  })

  it('the stepper accumulates taps from the last sent weight', async () => {
    const user = userEvent.setup()
    vi.mocked(getSession).mockResolvedValue(
      snapshot(1, [{ participantId: 'me', shares: 2, allocatedMinor: 833 }]),
    )
    renderClaimScreen()

    await user.click(
      await screen.findByRole('button', { name: /increase shares/i }),
    )
    expect(setSharesMock).toHaveBeenCalledWith(expect.anything(), 'i1', 3)

    // The snapshot still says 2, but the second tap steps from the sent 3 -
    // rapid taps inside the coalescing window must not resend the same weight.
    await user.click(screen.getByRole('button', { name: /increase shares/i }))
    expect(setSharesMock).toHaveBeenCalledWith(expect.anything(), 'i1', 4)

    await user.click(screen.getByRole('button', { name: /decrease shares/i }))
    expect(setSharesMock).toHaveBeenCalledWith(expect.anything(), 'i1', 3)
  })

  it('an inbound snapshot re-renders shares, behind the version guard', async () => {
    renderClaimScreen()
    await screen.findByRole('button', { name: 'Mine' })

    pushSnapshot(
      snapshot(2, [{ participantId: 'me', shares: 2, allocatedMinor: 625 }]),
    )

    expect(await screen.findByLabelText('Shares')).toHaveTextContent('2')
    expect(screen.getByText(/your share/i)).toHaveTextContent('£6.25')

    // A stale snapshot (lower version) must not roll the UI backwards.
    pushSnapshot(
      snapshot(1, [{ participantId: 'me', shares: 5, allocatedMinor: 1250 }]),
    )

    expect(screen.getByLabelText('Shares')).toHaveTextContent('2')
  })
})
