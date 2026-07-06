import { useState } from 'react'
import { useNavigate } from 'react-router'

import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { resolveCode } from '@/lib/api/client'

// The short-code alphabet (no 0/O/1/I/L), so typed input is filtered to it
// (docs/02-domain-model.md#entities).
const ALPHABET = 'ABCDEFGHJKMNPQRSTUVWXYZ23456789'
const CODE_LENGTH = 6

// Join by code: filter to 6 uppercase chars, resolve, then land on the session
// (docs/09-ux-flows.md#2-join-by-code---join).
export function JoinByCode() {
  const navigate = useNavigate()
  const [code, setCode] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  function onChange(value: string) {
    const filtered = value
      .toUpperCase()
      .split('')
      .filter((c) => ALPHABET.includes(c))
      .join('')
      .slice(0, CODE_LENGTH)
    setCode(filtered)
    setError(null)
  }

  async function submit() {
    setBusy(true)
    setError(null)
    try {
      const sessionId = await resolveCode(code)
      void navigate(`/s/${sessionId}`)
    } catch {
      setError("That code didn't match an active split.")
      setBusy(false)
    }
  }

  return (
    <main className="mx-auto flex min-h-svh max-w-md flex-col justify-center gap-6 p-6">
      <div className="space-y-2 text-center">
        <h1 className="text-2xl font-semibold tracking-tight">Join a split</h1>
        <p className="text-muted-foreground">
          Enter the 6-character code from the host.
        </p>
      </div>

      <form
        className="flex flex-col gap-4"
        onSubmit={(e) => {
          e.preventDefault()
          if (code.length === CODE_LENGTH && !busy) {
            void submit()
          }
        }}
      >
        <Input
          value={code}
          onChange={(e) => onChange(e.target.value)}
          inputMode="text"
          autoCapitalize="characters"
          autoFocus
          aria-label="Split code"
          aria-invalid={error !== null}
          className="text-center font-mono text-2xl tracking-[0.4em] uppercase"
          placeholder="K7MPQ2"
        />
        {error ? (
          <p className="text-destructive text-center text-sm">{error}</p>
        ) : null}
        <Button
          type="submit"
          size="lg"
          disabled={code.length !== CODE_LENGTH || busy}
        >
          Join
        </Button>
      </form>
    </main>
  )
}
