import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createBrowserRouter, RouterProvider } from 'react-router'

import { Landing } from '@/routes/Landing'
import { Session } from '@/routes/Session'
import './index.css'

const queryClient = new QueryClient()

const router = createBrowserRouter([
  { path: '/', element: <Landing /> },
  { path: '/s/:sessionId', element: <Session /> },
])

const rootElement = document.getElementById('root')
if (!rootElement) {
  throw new Error('Root element #root not found')
}

createRoot(rootElement).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </StrictMode>,
)
