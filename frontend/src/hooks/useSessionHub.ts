import { useEffect, useMemo, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import type { HubConnection } from '@microsoft/signalr'

import { applySnapshot, sessionKey } from '@/hooks/useSession'
import { createConnection } from '@/lib/realtime/connection'
import {
  HubEvents,
  claimItem,
  onOcrStatusChanged,
  onSnapshot,
  setShares,
  unclaimItem,
} from '@/lib/realtime/contract'

export type HubStatus = 'connected' | 'reconnecting' | 'disconnected'

export type SessionHub = {
  /** Feeds the connection pill: live / reconnecting / offline
   * (docs/09-ux-flows.md#7-claim---state-open-the-main-screen-everyone). */
  status: HubStatus
  claimItem: (itemId: string) => Promise<void>
  unclaimItem: (itemId: string) => Promise<void>
  setShares: (itemId: string, shares: number) => Promise<void>
}

// Live session wiring for a participant with a token. Inbound snapshots land in
// the query cache under the version guard; outbound claim gestures ride the same
// connection and are never applied optimistically - the authoritative snapshot
// follows within the coalescing window (docs/08-frontend-design.md#server-state).
// Visitors without a token render from the REST snapshot only and never connect.
export function useSessionHub(
  sessionId: string,
  token: string | null,
): SessionHub {
  const queryClient = useQueryClient()
  const [status, setStatus] = useState<HubStatus>('disconnected')
  const connectionRef = useRef<HubConnection | null>(null)

  useEffect(() => {
    if (!token) {
      return
    }

    const connection = createConnection(sessionId, token)
    connectionRef.current = connection

    // Status callbacks can fire after this connection is torn down (stop() is
    // async); only the current connection may drive the pill.
    function setStatusIfCurrent(next: HubStatus) {
      if (connectionRef.current === connection) {
        setStatus(next)
      }
    }

    onSnapshot(connection, HubEvents.SnapshotUpdated, (snapshot) =>
      applySnapshot(queryClient, snapshot),
    )
    onSnapshot(connection, HubEvents.SessionFinalized, (snapshot) =>
      applySnapshot(queryClient, snapshot),
    )
    onOcrStatusChanged(connection, () => {
      void queryClient.invalidateQueries({ queryKey: sessionKey(sessionId) })
    })

    connection.onreconnecting(() => setStatusIfCurrent('reconnecting'))
    connection.onreconnected(() => setStatusIfCurrent('connected'))
    connection.onclose(() => setStatusIfCurrent('disconnected'))

    connection
      .start()
      .then(() => setStatusIfCurrent('connected'))
      .catch(() => {
        // A failed connect is not fatal: the REST snapshot still renders and
        // the pill offers a reload.
        setStatusIfCurrent('disconnected')
      })

    return () => {
      connectionRef.current = null
      setStatus('disconnected')
      void connection.stop()
    }
  }, [sessionId, token, queryClient])

  return useMemo(() => {
    // Without a live connection a gesture quietly resolves; the tap simply has
    // no effect until the pill shows live again.
    const withConnection = (invoke: (c: HubConnection) => Promise<void>) => {
      const connection = connectionRef.current
      return connection ? invoke(connection) : Promise.resolve()
    }

    return {
      status,
      claimItem: (itemId) => withConnection((c) => claimItem(c, itemId)),
      unclaimItem: (itemId) => withConnection((c) => unclaimItem(c, itemId)),
      setShares: (itemId, shares) =>
        withConnection((c) => setShares(c, itemId, shares)),
    }
  }, [status])
}
