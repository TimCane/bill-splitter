import { useEffect } from 'react'
import { useQueryClient } from '@tanstack/react-query'

import { applySnapshot, sessionKey } from '@/hooks/useSession'
import { SessionSnapshotSchema } from '@/lib/api/schemas'
import { createConnection } from '@/lib/realtime/connection'
import { HubEvents } from '@/lib/realtime/contract'

// Live session updates for a participant with a token. Snapshots land in the query
// cache under the version guard; the OcrStatusChanged hint just triggers a refetch
// as a backstop (docs/05-realtime-contract.md#server---client-events). Visitors
// without a token render from the REST snapshot only and never connect.
export function useSessionHub(sessionId: string, token: string | null): void {
  const queryClient = useQueryClient()

  useEffect(() => {
    if (!token) {
      return
    }

    const connection = createConnection(sessionId, token)

    function applyFromEvent(payload: unknown) {
      const result = SessionSnapshotSchema.safeParse(payload)
      if (result.success) {
        applySnapshot(queryClient, result.data)
      } else {
        console.warn('Ignoring an unparseable session snapshot', result.error)
      }
    }

    connection.on(HubEvents.SnapshotUpdated, applyFromEvent)
    connection.on(HubEvents.SessionFinalized, applyFromEvent)
    connection.on(HubEvents.OcrStatusChanged, () => {
      void queryClient.invalidateQueries({ queryKey: sessionKey(sessionId) })
    })

    connection.start().catch(() => {
      // A failed connect is not fatal: the REST snapshot still renders and
      // automatic reconnect keeps retrying.
    })

    return () => {
      void connection.stop()
    }
  }, [sessionId, token, queryClient])
}
