import { render, screen } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router'

import { Landing } from '@/routes/Landing'

function renderLanding() {
  const router = createMemoryRouter([{ path: '/', element: <Landing /> }])
  return render(<RouterProvider router={router} />)
}

describe('Landing', () => {
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
})
