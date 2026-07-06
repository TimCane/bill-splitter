import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { vi } from 'vitest'

import { Landing } from '@/routes/Landing'
import { ApiError, createSession } from '@/lib/api/client'
import { preprocess } from '@/lib/image'
import { readIdentity } from '@/hooks/useParticipantToken'

const navigate = vi.fn()

vi.mock('react-router', async (importOriginal) => ({
  ...(await importOriginal<typeof import('react-router')>()),
  useNavigate: () => navigate,
}))

vi.mock('@/lib/api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/api/client')>()),
  createSession: vi.fn(),
}))

vi.mock('@/lib/image', () => ({ preprocess: vi.fn() }))

const createSessionMock = vi.mocked(createSession)
const preprocessMock = vi.mocked(preprocess)

function renderLanding() {
  const router = createMemoryRouter([{ path: '/', element: <Landing /> }])
  return render(<RouterProvider router={router} />)
}

describe('Landing', () => {
  beforeEach(() => {
    navigate.mockReset()
    createSessionMock.mockReset()
    preprocessMock.mockReset()
    localStorage.clear()
    vi.stubGlobal('URL', {
      ...URL,
      createObjectURL: vi.fn(() => 'blob:preview'),
      revokeObjectURL: vi.fn(),
    })
  })

  it('renders the headline', () => {
    renderLanding()
    expect(
      screen.getByRole('heading', { name: /split the bill/i }),
    ).toBeInTheDocument()
  })

  it('links to the join-by-code screen', () => {
    renderLanding()
    expect(screen.getByRole('link', { name: /enter code/i })).toHaveAttribute(
      'href',
      '/join',
    )
  })

  it('previews a chosen photo, uploads it, stores the token and navigates', async () => {
    const user = userEvent.setup()
    preprocessMock.mockResolvedValue(new Blob(['jpeg'], { type: 'image/jpeg' }))
    createSessionMock.mockResolvedValue({
      sessionId: 'sess-1',
      participantId: 'host-1',
      participantToken: 'tok-1',
      displayName: 'Host',
    })
    const { container } = renderLanding()

    const input = container.querySelector(
      'input[type=file]',
    ) as HTMLInputElement
    await user.upload(
      input,
      new File(['bytes'], 'receipt.png', { type: 'image/png' }),
    )

    expect(screen.getByAltText('Receipt preview')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /use photo/i }))

    await waitFor(() => expect(navigate).toHaveBeenCalledWith('/s/sess-1'))
    expect(preprocessMock).toHaveBeenCalled()
    expect(readIdentity('sess-1')).toEqual({
      participantId: 'host-1',
      participantToken: 'tok-1',
    })
  })

  it('reports rate limiting and stays on the preview', async () => {
    const user = userEvent.setup()
    preprocessMock.mockResolvedValue(new Blob(['jpeg'], { type: 'image/jpeg' }))
    createSessionMock.mockRejectedValue(new ApiError('rate-limited', 429))
    const { container } = renderLanding()

    const input = container.querySelector(
      'input[type=file]',
    ) as HTMLInputElement
    await user.upload(
      input,
      new File(['bytes'], 'receipt.png', { type: 'image/png' }),
    )
    await user.click(screen.getByRole('button', { name: /use photo/i }))

    expect(
      await screen.findByText(/too many sessions from this network/i),
    ).toBeInTheDocument()
    expect(navigate).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: /use photo/i })).toBeEnabled()
  })

  it('reports a generic error on a failed upload', async () => {
    const user = userEvent.setup()
    preprocessMock.mockResolvedValue(new Blob(['jpeg'], { type: 'image/jpeg' }))
    createSessionMock.mockRejectedValue(new ApiError('network', 0))
    const { container } = renderLanding()

    const input = container.querySelector(
      'input[type=file]',
    ) as HTMLInputElement
    await user.upload(
      input,
      new File(['bytes'], 'receipt.png', { type: 'image/png' }),
    )
    await user.click(screen.getByRole('button', { name: /use photo/i }))

    expect(
      await screen.findByText(/couldn't start the split/i),
    ).toBeInTheDocument()
    expect(navigate).not.toHaveBeenCalled()
  })

  it('reports an unreadable photo without attempting an upload', async () => {
    const user = userEvent.setup()
    preprocessMock.mockRejectedValue(new Error('decode failed'))
    const { container } = renderLanding()

    const input = container.querySelector(
      'input[type=file]',
    ) as HTMLInputElement
    await user.upload(
      input,
      new File(['bytes'], 'receipt.heic', { type: 'image/heic' }),
    )
    await user.click(screen.getByRole('button', { name: /use photo/i }))

    expect(
      await screen.findByText(/couldn't read that photo/i),
    ).toBeInTheDocument()
    expect(createSessionMock).not.toHaveBeenCalled()
    expect(navigate).not.toHaveBeenCalled()
  })
})
