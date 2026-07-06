import { render, screen } from '@testing-library/react'
import { vi } from 'vitest'

import { ShareSheet } from '@/components/session/ShareSheet'

vi.mock('qrcode', () => ({
  default: {
    toDataURL: vi.fn().mockResolvedValue('data:image/png;base64,fake'),
  },
}))

describe('ShareSheet', () => {
  it('shows the short code, the join host and a rendered QR', async () => {
    render(
      <ShareSheet
        joinUrl="https://split.example.com/s/abc"
        shortCode="K7MPQ2"
        open
        onOpenChange={() => {}}
      />,
    )

    expect(screen.getByText('K7MPQ2')).toBeInTheDocument()
    expect(screen.getByText(/split\.example\.com\/join/)).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: /copy link/i }),
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /done/i })).toBeInTheDocument()
    expect(await screen.findByAltText(/qr code to join/i)).toBeInTheDocument()
  })
})
