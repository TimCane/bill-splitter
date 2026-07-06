import {
  CreateSessionResponseSchema,
  HealthSchema,
  JoinResponseSchema,
  OpenResponseSchema,
  ResolveCodeResponseSchema,
  SessionSnapshotSchema,
  type CreateSessionResponse,
  type Health,
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

/** Map a parsed problem+json body to an ApiError; `type` falls back to 'unknown'
 * when the body is missing or not the expected shape. */
function problemToApiError(body: unknown, status: number): ApiError {
  const problem = (body ?? {}) as { type?: string; detail?: string }
  return new ApiError(problem.type ?? 'unknown', status, problem.detail)
}

async function toApiError(response: Response): Promise<ApiError> {
  try {
    return problemToApiError(await response.json(), response.status)
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

/** Create a session from a preprocessed receipt image. Goes over XHR rather than
 * fetch because only XHR reports upload progress (docs/08-frontend-design.md#uploads);
 * `onProgress` gets a 0-1 fraction, or is left alone when the size is unknown. */
export function createSession(
  image: Blob,
  onProgress?: (fraction: number) => void,
): Promise<CreateSessionResponse> {
  const form = new FormData()
  form.append('image', image, 'receipt.jpg')

  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('POST', `${API_BASE}/api/v1/sessions`)
    xhr.responseType = 'json'

    if (onProgress) {
      xhr.upload.onprogress = (e) => {
        if (e.lengthComputable) {
          onProgress(e.loaded / e.total)
        }
      }
    }

    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        try {
          resolve(CreateSessionResponseSchema.parse(xhr.response))
        } catch {
          // A 2xx whose body isn't the expected shape - status 0 since no HTTP
          // error status applies; the caller only distinguishes 429 anyway.
          reject(new ApiError('invalid-response', 0))
        }
        return
      }
      reject(problemToApiError(xhr.response, xhr.status))
    }
    xhr.onerror = () => reject(new ApiError('network', 0))
    xhr.ontimeout = () => reject(new ApiError('timeout', 0))
    xhr.timeout = 120000
    xhr.send(form)
  })
}

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

/** Finalize the split. The optional address is sent once and never stored; the
 * response is the finalized snapshot (docs/04-api-contract.md#post-apiv1sessionssessionidfinalize). */
export function finalizeSplit(
  sessionId: string,
  token: string,
  email: string | null,
): Promise<SessionSnapshot> {
  return json(
    `${sessionBase(sessionId)}/finalize`,
    { method: 'POST', headers: jsonHeaders, body: JSON.stringify({ email }) },
    (d) => SessionSnapshotSchema.parse(d),
    token,
  )
}

/** The server capability flags. Read once to know whether to offer the summary
 * email field; a failure or a degraded 503 throws (like every other call) and
 * useCapabilities reads that as no email (docs/04-api-contract.md#get-healthz). */
export function getHealth(): Promise<Health> {
  return json('/healthz', { method: 'GET' }, (d) => HealthSchema.parse(d))
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
