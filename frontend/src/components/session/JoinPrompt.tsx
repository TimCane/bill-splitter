import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'

import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import type { Identity } from '@/hooks/useParticipantToken'
import { applySnapshot } from '@/hooks/useSession'
import { ApiError, joinSession } from '@/lib/api/client'
import { displayNameError, parseDisplayName } from '@/lib/displayName'

type Props = {
  sessionId: string
  onJoined: (identity: Identity) => void
}

// Visitor with no token while the split is Open: pick a name and join
// (docs/09-ux-flows.md#6-join-prompt-visitor-with-no-token-state-open). On
// success the identity lands in localStorage via onJoined and the same route
// re-renders as the Claim screen.
export function JoinPrompt({ sessionId, onJoined }: Props) {
  const queryClient = useQueryClient()
  const [name, setName] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  function join() {
    const trimmed = parseDisplayName(name)
    if (trimmed === null) {
      setError(displayNameError)
      return
    }

    setBusy(true)
    setError(null)
    joinSession(sessionId, trimmed)
      .then((joined) => {
        applySnapshot(queryClient, joined.snapshot)
        onJoined({
          participantId: joined.participantId,
          participantToken: joined.participantToken,
        })
      })
      .catch((cause: unknown) => {
        setBusy(false)
        if (cause instanceof ApiError && cause.type === 'session-full') {
          setError('This split already has 20 people.')
          return
        }

        if (cause instanceof ApiError && cause.type === 'wrong-state') {
          // Finalized while the visitor was typing: refetch and let the
          // route switch to the read-only summary.
          void queryClient.invalidateQueries()
          return
        }

        setError('Something went wrong. Try again.')
      })
  }

  return (
    <main className="mx-auto flex min-h-svh max-w-md flex-col items-center justify-center gap-6 p-6">
      <div className="space-y-2 text-center">
        <h1 className="text-xl font-semibold tracking-tight">
          Join this split
        </h1>
        <p className="text-muted-foreground">
          Pick a name the table will recognise.
        </p>
      </div>

      <form
        className="flex w-full flex-col gap-4"
        onSubmit={(event) => {
          event.preventDefault()
          join()
        }}
      >
        <div className="flex flex-col gap-2">
          <Label htmlFor="join-name">Your name</Label>
          <Input
            id="join-name"
            value={name}
            onChange={(event) => setName(event.target.value)}
            maxLength={30}
            autoFocus
          />
          {error ? <p className="text-destructive text-sm">{error}</p> : null}
        </div>
        <Button type="submit" disabled={busy}>
          Join the split
        </Button>
      </form>
    </main>
  )
}
