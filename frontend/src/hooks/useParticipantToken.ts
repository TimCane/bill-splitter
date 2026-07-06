import { useCallback, useState } from 'react'

// Per-session identity in localStorage under `bs:{sessionId}` so being in two
// sessions on one phone works (docs/08-frontend-design.md#identity). The raw token
// is the credential; it is written after create/join and read on mount.
export type Identity = {
  participantId: string
  participantToken: string
}

type StoredIdentity = Identity & { storedAt: number }

const keyFor = (sessionId: string) => `bs:${sessionId}`

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
