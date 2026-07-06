// The optional summary address in the finalize dialog. The server never returns
// it, so validation and the masked confirmation are client-side only
// (docs/09-ux-flows.md#8-summary---state-finalized).
export const emailError = 'Enter a valid email address.'

const pattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

/** The trimmed address, or null when it is not a plausible email. */
export function parseEmail(raw: string): string | null {
  const trimmed = raw.trim()
  return pattern.test(trimmed) ? trimmed : null
}

/** Mask an address to `t***@e***.com` for the sent confirmation - just enough to
 * recognise which address it went to, never the whole thing. */
export function maskEmail(address: string): string {
  const [local, domain] = address.split('@')
  if (!domain) {
    return address
  }

  const dot = domain.lastIndexOf('.')
  const head = dot >= 0 ? domain.slice(0, dot) : domain
  const tld = dot >= 0 ? domain.slice(dot) : ''
  return `${maskPart(local ?? '')}@${maskPart(head)}${tld}`
}

function maskPart(part: string): string {
  return part.length === 0 ? part : `${part[0]}***`
}
