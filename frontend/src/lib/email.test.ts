import { describe, expect, it } from 'vitest'

import { maskEmail, parseEmail } from '@/lib/email'

describe('parseEmail', () => {
  it('accepts a plausible address and trims it', () => {
    expect(parseEmail('  tim@example.com ')).toBe('tim@example.com')
  })

  it('rejects a malformed address', () => {
    expect(parseEmail('tim@')).toBeNull()
    expect(parseEmail('not an email')).toBeNull()
    expect(parseEmail('')).toBeNull()
  })
})

describe('maskEmail', () => {
  it('masks the local part and domain but keeps the tld', () => {
    expect(maskEmail('tim@example.com')).toBe('t***@e***.com')
  })

  it('handles a multi-level domain', () => {
    expect(maskEmail('sam@mail.example.co.uk')).toBe('s***@m***.uk')
  })
})
