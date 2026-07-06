import { z } from 'zod'

// Zod mirrors of the backend DTOs (docs/04-api-contract.md#sessionsnapshotdto).
// Every inbound payload - REST responses and hub events - is parsed through these
// so backend/frontend drift fails loudly (docs/08-frontend-design.md#validation).

export const SessionStateSchema = z.enum([
  'Processing',
  'Review',
  'Open',
  'Finalized',
])
export type SessionState = z.infer<typeof SessionStateSchema>

export const OcrStatusSchema = z.enum([
  'Pending',
  'Processing',
  'Done',
  'Failed',
])
export type OcrStatus = z.infer<typeof OcrStatusSchema>

export const OcrSchema = z.object({
  status: OcrStatusSchema,
  failureReason: z.string().nullable(),
})

export const ParticipantSchema = z.object({
  participantId: z.string(),
  displayName: z.string(),
  isHost: z.boolean(),
})
export type Participant = z.infer<typeof ParticipantSchema>

export const ClaimSchema = z.object({
  participantId: z.string(),
  shares: z.number().int(),
  allocatedMinor: z.number().int(),
})

export const ItemSchema = z.object({
  itemId: z.string(),
  name: z.string(),
  quantity: z.number().int(),
  priceMinor: z.number().int(),
  claims: z.array(ClaimSchema),
})
export type Item = z.infer<typeof ItemSchema>

export const BillSchema = z.object({
  subtotalMinor: z.number().int(),
  taxMinor: z.number().int(),
  tipMinor: z.number().int(),
  serviceMinor: z.number().int(),
  totalMinor: z.number().int(),
  checksumMinor: z.number().int(),
})
export type Bill = z.infer<typeof BillSchema>

export const ParticipantTotalSchema = z.object({
  participantId: z.string(),
  itemsMinor: z.number().int(),
  taxMinor: z.number().int(),
  tipMinor: z.number().int(),
  serviceMinor: z.number().int(),
  unclaimedMinor: z.number().int(),
  totalMinor: z.number().int(),
})

export const SessionSnapshotSchema = z.object({
  sessionId: z.string(),
  version: z.number().int(),
  state: SessionStateSchema,
  currency: z.string(),
  expiresAt: z.string(),
  shortCode: z.string().nullable(),
  joinUrl: z.string().nullable(),
  hostParticipantId: z.string(),
  ocr: OcrSchema,
  participants: z.array(ParticipantSchema),
  items: z.array(ItemSchema),
  bill: BillSchema,
  unclaimedTotalMinor: z.number().int(),
  totals: z.array(ParticipantTotalSchema),
})
export type SessionSnapshot = z.infer<typeof SessionSnapshotSchema>

export const OpenResponseSchema = z.object({
  shortCode: z.string(),
  joinUrl: z.string(),
})
export type OpenResponse = z.infer<typeof OpenResponseSchema>

export const ResolveCodeResponseSchema = z.object({
  sessionId: z.string(),
})
