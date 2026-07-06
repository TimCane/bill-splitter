import { useCallback, useState } from 'react'

// Per-session identity in localStorage under `bs:{sessionId}` so being in two
// sessions on one phone works (docs/08-frontend-design.md#identity). The raw token
// is the credential; it is written after create/join and read on mount.
export type Identity = {
  participantId: string
  participantToken: string
}

type StoredIdentity = Identity & { storedAt: number }

const keyPrefix = 'bs:'
const keyFor = (sessionId: string) => `${keyPrefix}${sessionId}`

// Sessions live at most 24h; anything older than 25h is a dead session's
// credential. One extra hour of slack keeps a clock skew from pruning a live one.
const maxAgeMs = 25 * 60 * 60 * 1000

export function readIdentity(sessionId: string): Identity | null {
  const raw = localStorage.getItem(keyFor(sessionId))
  if (!raw) {
    return null
  }

  try {
    const parsed = JSON.parse(raw) as Partial<StoredIdentity>
    if (
      typeof parsed.participantId === 'string' &&
      typeof parsed.participantToken === 'string'
    ) {
      return {
        participantId: parsed.participantId,
        participantToken: parsed.participantToken,
      }
    }
  } catch {
    // Corrupt entry - treat as no identity.
  }

  return null
}

export function storeIdentity(
  sessionId: string,
  identity: Identity,
  now: number,
): void {
  const stored: StoredIdentity = { ...identity, storedAt: now }
  localStorage.setItem(keyFor(sessionId), JSON.stringify(stored))
}

/** Drop identities for sessions that must have expired. Called once at app
 * bootstrap (docs/08-frontend-design.md#identity) - there is no other cleanup,
 * so this is what keeps localStorage from accumulating dead tokens. */
export function pruneStaleIdentities(now: number): void {
  const stale: string[] = []
  for (let i = 0; i < localStorage.length; i++) {
    const key = localStorage.key(i)
    if (!key?.startsWith(keyPrefix)) {
      continue
    }

    let storedAt: unknown
    try {
      storedAt = (JSON.parse(localStorage.getItem(key) ?? '') as StoredIdentity)
        .storedAt
    } catch {
      // Corrupt entry - age unknowable, treat as stale.
    }
    if (typeof storedAt !== 'number' || now - storedAt > maxAgeMs) {
      stale.push(key)
    }
  }

  for (const key of stale) {
    localStorage.removeItem(key)
  }
}

export function useParticipantToken(sessionId: string) {
  const [identity, setIdentity] = useState<Identity | null>(() =>
    readIdentity(sessionId),
  )

  const store = useCallback(
    (next: Identity) => {
      storeIdentity(sessionId, next, Date.now())
      setIdentity(next)
    },
    [sessionId],
  )

  return { identity, store }
}
