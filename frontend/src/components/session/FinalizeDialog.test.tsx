import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { vi } from 'vitest'

import { ClaimScreen } from '@/components/session/ClaimScreen'
import type { SessionHub } from '@/hooks/useSessionHub'
import { finalizeSplit } from '@/lib/api/client'
import type { SessionSnapshot } from '@/lib/api/schemas'

// The capability flag is exercised on its own in useCapabilities; here the relay
// is assumed present so the email field renders.
vi.mock('@/hooks/useCapabilities', () => ({
  useCapabilities: () => ({ emailEnabled: true }),
}))

vi.mock('@/lib/api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/api/client')>()),
  finalizeSplit: vi.fn(),
}))

const hub: SessionHub = {
  status: 'connected',
  claimItem: vi.fn(() => Promise.resolve()),
  unclaimItem: vi.fn(() => Promise.resolve()),
  setShares: vi.fn(() => Promise.resolve()),
}

function snapshot(state: SessionSnapshot['state'] = 'Open'): SessionSnapshot {
  return {
    sessionId: 's1',
    version: 4,
    state,
    currency: 'GBP',
    expiresAt: '2026-07-06T20:00:00Z',
    shortCode: 'K7MPQ2',
    joinUrl: 'https://split.example.com/s/s1',
    hostParticipantId: 'host',
    ocr: { status: 'Done', failureReason: null, warnings: [] },
    participants: [
      { participantId: 'host', displayName: 'Tim', isHost: true },
      { participantId: 'sam', displayName: 'Sam', isHost: false },
    ],
    items: [],
    bill: {
      subtotalMinor: 650,
      taxMinor: 0,
      tipMinor: 0,
      serviceMinor: 0,
      totalMinor: 650,
      checksumMinor: 0,
    },
    unclaimedTotalMinor: 650,
    totals: [],
  }
}

function renderClaim(onEmailedSummary = vi.fn()) {
  render(
    <QueryClientProvider client={new QueryClient()}>
      <ClaimScreen
        snapshot={snapshot()}
        identity={{ participantId: 'host', participantToken: 'token' }}
        isHost
        hub={hub}
        onEmailedSummary={onEmailedSummary}
      />
    </QueryClientProvider>,
  )
  return onEmailedSummary
}

describe('finalize dialog', () => {
  beforeEach(() => vi.clearAllMocks())

  it('shows the unclaimed copy and the optional email field', async () => {
    const user = userEvent.setup()
    renderClaim()

    await user.click(screen.getByRole('button', { name: 'Finalize' }))

    expect(
      screen.getByRole('heading', { name: /lock the split/i }),
    ).toBeInTheDocument()
    expect(screen.getByText(/unclaimed £6\.50 gets split/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/email me the summary/i)).toBeInTheDocument()
  })

  it('sends the address and reports the masked confirmation', async () => {
    const user = userEvent.setup()
    vi.mocked(finalizeSplit).mockResolvedValue(snapshot('Finalized'))
    const onEmailedSummary = renderClaim()

    await user.click(screen.getByRole('button', { name: 'Finalize' }))
    await user.type(
      screen.getByLabelText(/email me the summary/i),
      'tim@example.com',
    )
    await user.click(screen.getByRole('button', { name: /lock the split/i }))

    await waitFor(() =>
      expect(finalizeSplit).toHaveBeenCalledWith(
        's1',
        'token',
        'tim@example.com',
      ),
    )
    expect(onEmailedSummary).toHaveBeenCalledWith('t***@e***.com')
  })

  it('rejects a malformed address before sending', async () => {
    const user = userEvent.setup()
    const onEmailedSummary = renderClaim()

    await user.click(screen.getByRole('button', { name: 'Finalize' }))
    await user.type(screen.getByLabelText(/email me the summary/i), 'nope')
    await user.click(screen.getByRole('button', { name: /lock the split/i }))

    expect(screen.getByText(/valid email address/i)).toBeInTheDocument()
    expect(finalizeSplit).not.toHaveBeenCalled()
    expect(onEmailedSummary).not.toHaveBeenCalled()
  })
})
