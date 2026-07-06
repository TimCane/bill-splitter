import { z } from 'zod'

import { OcrStatusSchema } from '@/lib/api/schemas'

// The hub contract, hand-mirrored once (SignalR has no codegen) and parsed with Zod
// so backend drift fails loudly (docs/05-realtime-contract.md#typing-on-the-frontend).
export const HubEvents = {
  SnapshotUpdated: 'SnapshotUpdated',
  OcrStatusChanged: 'OcrStatusChanged',
  SessionFinalized: 'SessionFinalized',
} as const

export const OcrStatusChangedSchema = z.object({
  status: OcrStatusSchema,
  failureReason: z.string().nullable(),
})
export type OcrStatusChanged = z.infer<typeof OcrStatusChangedSchema>
