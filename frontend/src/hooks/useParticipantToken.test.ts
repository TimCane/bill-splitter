import { beforeEach, describe, expect, it } from 'vitest'

import {
  pruneStaleIdentities,
  readIdentity,
  storeIdentity,
} from '@/hooks/useParticipantToken'

const hour = 60 * 60 * 1000
const identity = { participantId: 'p1', participantToken: 't1' }

describe('identity storage', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it('round-trips an identity per session', () => {
    storeIdentity('s1', identity, Date.now())

    expect(readIdentity('s1')).toEqual(identity)
    expect(readIdentity('s2')).toBeNull()
  })

  it('prunes entries older than 25h and keeps fresh ones', () => {
    const now = Date.now()
    storeIdentity('old', identity, now - 26 * hour)
    storeIdentity('fresh', identity, now - 1 * hour)

    pruneStaleIdentities(now)

    expect(readIdentity('old')).toBeNull()
    expect(readIdentity('fresh')).toEqual(identity)
  })

  it('prunes corrupt entries and leaves foreign keys alone', () => {
    localStorage.setItem('bs:corrupt', 'not json')
    localStorage.setItem('unrelated', 'not json')

    pruneStaleIdentities(Date.now())

    expect(localStorage.getItem('bs:corrupt')).toBeNull()
    expect(localStorage.getItem('unrelated')).toBe('not json')
  })
})
