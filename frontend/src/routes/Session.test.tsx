import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { vi } from 'vitest'

import { useParticipantToken } from '@/hooks/useParticipantToken'
import { useSession } from '@/hooks/useSession'
import { ApiError } from '@/lib/api/client'
import type { SessionSnapshot, SessionState } from '@/lib/api/schemas'
import { Session } from '@/routes/Session'

vi.mock('react-router', async (importOriginal) => ({
  ...(await importOriginal<typeof import('react-router')>()),
  useParams: () => ({ sessionId: 's1' }),
}))

vi.mock('qrcode', () => ({
  default: {
    toDataURL: vi.fn().mockResolvedValue('data:image/png;base64,fake'),
  },
}))

vi.mock('@/hooks/useSession', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/hooks/useSession')>()),
  useSession: vi.fn(),
}))

vi.mock('@/hooks/useSessionHub', () => ({
  useSessionHub: () => ({
    status: 'connected' as const,
    claimItem: vi.fn(),
    unclaimItem: vi.fn(),
    setShares: vi.fn(),
  }),
}))

vi.mock('@/hooks/useParticipantToken', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/hooks/useParticipantToken')>()),
  useParticipantToken: vi.fn(),
}))

function snapshot(state: SessionState): SessionSnapshot {
  return {
    sessionId: 's1',
    version: 3,
    state,
    currency: 'GBP',
    expiresAt: '2026-07-06T20:00:00Z',
    shortCode: state === 'Open' ? 'K7MPQ2' : null,
    joinUrl: state === 'Open' ? 'https://split.example.com/s/s1' : null,
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
        claims: [],
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

function givenIdentity(participantId: string | null) {
  vi.mocked(useParticipantToken).mockReturnValue({
    identity: participantId
      ? { participantId, participantToken: 'token' }
      : null,
    store: vi.fn(),
  })
}

function givenSnapshot(data: SessionSnapshot) {
  vi.mocked(useSession).mockReturnValue({
    isPending: false,
    isError: false,
    data,
  } as unknown as ReturnType<typeof useSession>)
}

function givenError(error: Error) {
  vi.mocked(useSession).mockReturnValue({
    isPending: false,
    isError: true,
    error,
  } as unknown as ReturnType<typeof useSession>)
}

function renderSession() {
  return render(
    <MemoryRouter>
      <QueryClientProvider client={new QueryClient()}>
        <Session />
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

// The route/state/role matrix (docs/09-ux-flows.md#routestaterole-matrix): every
// (state, isHost, hasToken) cell lands on its screen without navigation.
describe('Session state matrix', () => {
  it('Processing: host sees the processing screen', () => {
    givenIdentity('host')
    givenSnapshot(snapshot('Processing'))
    renderSession()

    expect(screen.getByText(/reading your receipt/i)).toBeInTheDocument()
  })

  it('Processing: a visitor sees the holding card', () => {
    givenIdentity(null)
    givenSnapshot(snapshot('Processing'))
    renderSession()

    expect(screen.getByText(/isn't open yet/i)).toBeInTheDocument()
  })

  it('Review: the host sees the review screen', () => {
    givenIdentity('host')
    givenSnapshot(snapshot('Review'))
    renderSession()

    expect(
      screen.getByRole('heading', { name: /check the items/i }),
    ).toBeInTheDocument()
  })

  it('Review: a non-host participant sees the holding card', () => {
    givenIdentity('me')
    givenSnapshot(snapshot('Review'))
    renderSession()

    expect(screen.getByText(/isn't open yet/i)).toBeInTheDocument()
  })

  it('Open: a participant with a token sees the claim screen', () => {
    givenIdentity('me')
    givenSnapshot(snapshot('Open'))
    renderSession()

    expect(screen.getByText('Split K7MPQ2')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Mine' })).toBeInTheDocument()
  })

  it('Open: the host sees the claim screen with the share entry point', () => {
    givenIdentity('host')
    givenSnapshot(snapshot('Open'))
    renderSession()

    expect(screen.getByText('Split K7MPQ2')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /show qr/i })).toBeInTheDocument()
  })

  it('Open: a visitor without a token sees the join prompt', () => {
    givenIdentity(null)
    givenSnapshot(snapshot('Open'))
    renderSession()

    expect(
      screen.getByRole('button', { name: /join the split/i }),
    ).toBeInTheDocument()
  })

  it('Finalized: everyone sees the locked summary', () => {
    givenIdentity(null)
    givenSnapshot(snapshot('Finalized'))
    renderSession()

    expect(screen.getByText(/split locked/i)).toBeInTheDocument()
  })

  it('404: everyone sees the expired card', () => {
    givenIdentity('me')
    givenError(new ApiError('session-not-found', 404))
    renderSession()

    expect(screen.getByText(/this split has expired/i)).toBeInTheDocument()
  })
})
