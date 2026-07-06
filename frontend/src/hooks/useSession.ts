import { useQuery, type QueryClient } from '@tanstack/react-query'

import { getSession } from '@/lib/api/client'
import type { SessionSnapshot } from '@/lib/api/schemas'

// One source of truth: the query cache entry ['session', id] holding the latest
// snapshot (docs/08-frontend-design.md#server-state). Seeded by REST; live hub
// updates land here too (wired in the join/processing work).
export const sessionKey = (sessionId: string) => ['session', sessionId] as const

export function useSession(sessionId: string) {
  return useQuery({
    queryKey: sessionKey(sessionId),
    queryFn: () => getSession(sessionId),
  })
}

/** Write a fresh snapshot into the cache, honouring the version guard so an
 * out-of-order update never rolls the UI backwards
 * (docs/05-realtime-contract.md#ordering-and-idempotency). */
export function applySnapshot(
  queryClient: QueryClient,
  snapshot: SessionSnapshot,
): void {
  queryClient.setQueryData<SessionSnapshot>(
    sessionKey(snapshot.sessionId),
    (current) =>
      current && current.version > snapshot.version ? current : snapshot,
  )
}
