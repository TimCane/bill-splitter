import { HubConnectionBuilder, type HubConnection } from '@microsoft/signalr'

// SignalR connection factory (docs/05-realtime-contract.md#connecting). The token
// rides the standard access_token query slot; sessionId is a query param the auth
// handler reads. withAutomaticReconnect replays the same handshake, and the
// post-connect SnapshotUpdated heals any gap.
const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? ''

export function createConnection(
  sessionId: string,
  token: string,
): HubConnection {
  const url = `${API_BASE}/hubs/session?sessionId=${encodeURIComponent(sessionId)}`
  return new HubConnectionBuilder()
    .withUrl(url, { accessTokenFactory: () => token })
    .withAutomaticReconnect()
    .build()
}
