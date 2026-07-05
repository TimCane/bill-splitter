import { render, screen } from '@testing-library/react'

import { Landing } from '@/routes/Landing'

describe('Landing', () => {
  it('renders the headline', () => {
    render(<Landing />)
    expect(
      screen.getByRole('heading', { name: /split the bill/i }),
    ).toBeInTheDocument()
  })
})
