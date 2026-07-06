import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createBrowserRouter, RouterProvider } from 'react-router'

import { pruneStaleIdentities } from '@/hooks/useParticipantToken'
import { JoinByCode } from '@/routes/JoinByCode'
import { Landing } from '@/routes/Landing'
import { Session } from '@/routes/Session'
import './index.css'

// Prune at bootstrap, not on a route: guests deep-link straight into
// /s/:sessionId from the QR and would never pass through Landing
// (docs/08-frontend-design.md#identity).
pruneStaleIdentities(Date.now())

const queryClient = new QueryClient()

const router = createBrowserRouter([
  { path: '/', element: <Landing /> },
  { path: '/join', element: <JoinByCode /> },
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
