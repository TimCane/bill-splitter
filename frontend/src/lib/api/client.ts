import {
  JoinResponseSchema,
  OpenResponseSchema,
  ResolveCodeResponseSchema,
  SessionSnapshotSchema,
  type JoinResponse,
  type OpenResponse,
  type SessionSnapshot,
} from '@/lib/api/schemas'

// Hand-written REST client over the lifecycle endpoints (docs/04-api-contract.md).
// Claim gestures go over the hub, not here. The base URL is same-origin in prod;
// dev points at the API via VITE_API_BASE_URL.
const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? ''

/** A parsed problem+json error; `type` is the stable machine code the UI switches on. */
export class ApiError extends Error {
  readonly type: string
  readonly status: number

  constructor(type: string, status: number, detail?: string) {
    super(detail ?? type)
    this.type = type
    this.status = status
  }
}

type ItemBody = { name: string; quantity: number; priceMinor: number }
type BillBody = {
  taxMinor: number
  tipMinor: number
  serviceMinor: number
  totalMinor: number
  currency: string
}

async function request(
  path: string,
  init: RequestInit,
  token?: string,
): Promise<Response> {
  const headers = new Headers(init.headers)
  if (token) {
    headers.set('Authorization', `Bearer ${token}`)
  }

  const response = await fetch(`${API_BASE}${path}`, { ...init, headers })
  if (!response.ok) {
    throw await toApiError(response)
  }

  return response
}

async function toApiError(response: Response): Promise<ApiError> {
  try {
    const problem = (await response.json()) as {
      type?: string
      detail?: string
    }
    return new ApiError(
      problem.type ?? 'unknown',
      response.status,
      problem.detail,
    )
  } catch {
    return new ApiError('unknown', response.status)
  }
}

async function json<T>(
  path: string,
  init: RequestInit,
  parse: (data: unknown) => T,
  token?: string,
): Promise<T> {
  const response = await request(path, init, token)
  return parse(await response.json())
}

const jsonHeaders = { 'Content-Type': 'application/json' }
const sessionBase = (sessionId: string) => `/api/v1/sessions/${sessionId}`

export function getSession(sessionId: string): Promise<SessionSnapshot> {
  return json(sessionBase(sessionId), { method: 'GET' }, (d) =>
    SessionSnapshotSchema.parse(d),
  )
}

/** Join an open session. Anonymous; the returned token is the caller's
 * credential thereafter and goes straight into localStorage
 * (docs/04-api-contract.md#post-apiv1sessionssessionidparticipants). */
export function joinSession(
  sessionId: string,
  displayName: string,
): Promise<JoinResponse> {
  return json(
    `${sessionBase(sessionId)}/participants`,
    {
      method: 'POST',
      headers: jsonHeaders,
      body: JSON.stringify({ displayName }),
    },
    (d) => JoinResponseSchema.parse(d),
  )
}

export function renameMe(
  sessionId: string,
  token: string,
  displayName: string,
): Promise<SessionSnapshot> {
  return json(
    `${sessionBase(sessionId)}/participants/me`,
    {
      method: 'PUT',
      headers: jsonHeaders,
      body: JSON.stringify({ displayName }),
    },
    (d) => SessionSnapshotSchema.parse(d),
    token,
  )
}

export function addItem(
  sessionId: string,
  token: string,
  body: ItemBody,
): Promise<SessionSnapshot> {
  return json(
    `${sessionBase(sessionId)}/items`,
    { method: 'POST', headers: jsonHeaders, body: JSON.stringify(body) },
    (d) => SessionSnapshotSchema.parse(d),
    token,
  )
}

export function updateItem(
  sessionId: string,
  token: string,
  itemId: string,
  body: ItemBody,
): Promise<SessionSnapshot> {
  return json(
    `${sessionBase(sessionId)}/items/${itemId}`,
    { method: 'PUT', headers: jsonHeaders, body: JSON.stringify(body) },
    (d) => SessionSnapshotSchema.parse(d),
    token,
  )
}

export function deleteItem(
  sessionId: string,
  token: string,
  itemId: string,
): Promise<SessionSnapshot> {
  return json(
    `${sessionBase(sessionId)}/items/${itemId}`,
    { method: 'DELETE' },
    (d) => SessionSnapshotSchema.parse(d),
    token,
  )
}

export function setBill(
  sessionId: string,
  token: string,
  body: BillBody,
): Promise<SessionSnapshot> {
  return json(
    `${sessionBase(sessionId)}/bill`,
    { method: 'PUT', headers: jsonHeaders, body: JSON.stringify(body) },
    (d) => SessionSnapshotSchema.parse(d),
    token,
  )
}

export function openSplit(
  sessionId: string,
  token: string,
): Promise<OpenResponse> {
  return json(
    `${sessionBase(sessionId)}/open`,
    { method: 'POST' },
    (d) => OpenResponseSchema.parse(d),
    token,
  )
}

export function resolveCode(shortCode: string): Promise<string> {
  return json(
    `/api/v1/codes/${shortCode}`,
    { method: 'GET' },
    (d) => ResolveCodeResponseSchema.parse(d).sessionId,
  )
}

/** Fetch the host-only receipt image (needs the bearer token) as an object URL. */
export async function fetchReceiptObjectUrl(
  sessionId: string,
  token: string,
): Promise<string> {
  const response = await request(
    `${sessionBase(sessionId)}/receipt`,
    { method: 'GET' },
    token,
  )
  return URL.createObjectURL(await response.blob())
}
