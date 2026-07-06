import type { HubConnection } from '@microsoft/signalr'
import { z } from 'zod'

import {
  OcrStatusSchema,
  SessionSnapshotSchema,
  type SessionSnapshot,
} from '@/lib/api/schemas'

// The hub contract, hand-mirrored once (SignalR has no codegen) and parsed with Zod
// so backend drift fails loudly (docs/05-realtime-contract.md#typing-on-the-frontend).
// Everything that touches the raw connection goes through here - tests mock this
// module, not @microsoft/signalr (docs/11-testing-strategy.md#frontend-vitest--rtl).
export const HubEvents = {
  SnapshotUpdated: 'SnapshotUpdated',
  OcrStatusChanged: 'OcrStatusChanged',
  SessionFinalized: 'SessionFinalized',
} as const

export const HubMethods = {
  ClaimItem: 'ClaimItem',
  UnclaimItem: 'UnclaimItem',
  SetShares: 'SetShares',
} as const

export const OcrStatusChangedSchema = z.object({
  status: OcrStatusSchema,
  failureReason: z.string().nullable(),
})
export type OcrStatusChanged = z.infer<typeof OcrStatusChangedSchema>

// --- Client -> server gestures (docs/05-realtime-contract.md#client---server-methods)

/** Upsert my claim with one share. */
export function claimItem(
  connection: HubConnection,
  itemId: string,
): Promise<void> {
  return connection.invoke(HubMethods.ClaimItem, itemId)
}

/** Remove my claim; the server no-ops if I hold none. */
export function unclaimItem(
  connection: HubConnection,
  itemId: string,
): Promise<void> {
  return connection.invoke(HubMethods.UnclaimItem, itemId)
}

/** Upsert my claim at the given weight (1-99). */
export function setShares(
  connection: HubConnection,
  itemId: string,
  shares: number,
): Promise<void> {
  return connection.invoke(HubMethods.SetShares, itemId, shares)
}

// --- Server -> client events (docs/05-realtime-contract.md#server---client-events)

/** Subscribe to a snapshot-carrying event. The payload is Zod-parsed; a payload
 * that no longer matches the schema is dropped with a warning rather than fed to
 * the UI (docs/08-frontend-design.md#validation). */
export function onSnapshot(
  connection: HubConnection,
  event: typeof HubEvents.SnapshotUpdated | typeof HubEvents.SessionFinalized,
  handler: (snapshot: SessionSnapshot) => void,
): void {
  connection.on(event, (payload: unknown) => {
    const result = SessionSnapshotSchema.safeParse(payload)
    if (result.success) {
      handler(result.data)
    } else {
      console.warn(`Ignoring an unparseable ${event} payload`, result.error)
    }
  })
}

/** Subscribe to the OCR transition hint. Advisory only - every transition is
 * paired with an authoritative SnapshotUpdated. */
export function onOcrStatusChanged(
  connection: HubConnection,
  handler: (hint: OcrStatusChanged) => void,
): void {
  connection.on(HubEvents.OcrStatusChanged, (payload: unknown) => {
    const result = OcrStatusChangedSchema.safeParse(payload)
    if (result.success) {
      handler(result.data)
    } else {
      console.warn('Ignoring an unparseable OcrStatusChanged payload')
    }
  })
}
