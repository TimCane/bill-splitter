import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'

import { JoinByCode } from '@/routes/JoinByCode'
import { ApiError, resolveCode } from '@/lib/api/client'

const navigate = vi.fn()

vi.mock('react-router', async (importOriginal) => ({
  ...(await importOriginal<typeof import('react-router')>()),
  useNavigate: () => navigate,
}))

vi.mock('@/lib/api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/api/client')>()),
  resolveCode: vi.fn(),
}))

const resolveCodeMock = vi.mocked(resolveCode)

describe('JoinByCode', () => {
  beforeEach(() => {
    navigate.mockReset()
    resolveCodeMock.mockReset()
  })

  it('filters input to the code alphabet and enables Join at 6 chars', async () => {
    const user = userEvent.setup()
    render(<JoinByCode />)

    const input = screen.getByLabelText('Split code')
    await user.type(input, 'k7!mpq2zz')

    // Lowercased + uppercased, non-alphabet stripped, capped at 6.
    expect(input).toHaveValue('K7MPQ2')
    expect(screen.getByRole('button', { name: /join/i })).toBeEnabled()
  })

  it('resolves the code and navigates to the session', async () => {
    const user = userEvent.setup()
    resolveCodeMock.mockResolvedValue('sess-1')
    render(<JoinByCode />)

    await user.type(screen.getByLabelText('Split code'), 'K7MPQ2')
    await user.click(screen.getByRole('button', { name: /join/i }))

    expect(resolveCodeMock).toHaveBeenCalledWith('K7MPQ2')
    expect(navigate).toHaveBeenCalledWith('/s/sess-1')
  })

  it('shows the inline error on an unknown code', async () => {
    const user = userEvent.setup()
    resolveCodeMock.mockRejectedValue(new Error('nope'))
    render(<JoinByCode />)

    await user.type(screen.getByLabelText('Split code'), 'ZZZZZZ')
    await user.click(screen.getByRole('button', { name: /join/i }))

    expect(
      await screen.findByText("That code didn't match an active split."),
    ).toBeInTheDocument()
    expect(navigate).not.toHaveBeenCalled()
  })

  it('reports rate limiting rather than a wrong code on a 429', async () => {
    const user = userEvent.setup()
    resolveCodeMock.mockRejectedValue(new ApiError('rate-limited', 429))
    render(<JoinByCode />)

    await user.type(screen.getByLabelText('Split code'), 'K7MPQ2')
    await user.click(screen.getByRole('button', { name: /join/i }))

    expect(
      await screen.findByText(/too many attempts/i),
    ).toBeInTheDocument()
    expect(navigate).not.toHaveBeenCalled()
  })
})
