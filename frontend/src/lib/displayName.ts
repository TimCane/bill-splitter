// The display-name rule from docs/04-api-contract.md, shared by every form
// that submits one. The server enforces it too; this is the inline feedback.
export const displayNameError = 'Use 1 to 30 characters.'

/** The trimmed name, or null when it breaks the 1-30 character rule. */
export function parseDisplayName(raw: string): string | null {
  const trimmed = raw.trim()
  return trimmed.length < 1 || trimmed.length > 30 ? null : trimmed
}
