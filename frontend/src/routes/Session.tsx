import { Link, useParams } from 'react-router'

import { Button } from '@/components/ui/button'
import { OpenHostScreen } from '@/components/session/OpenHostScreen'
import { ProcessingScreen } from '@/components/session/ProcessingScreen'
import { ReviewScreen } from '@/components/session/ReviewScreen'
import { useParticipantToken } from '@/hooks/useParticipantToken'
import { useSession } from '@/hooks/useSession'
import { useSessionHub } from '@/hooks/useSessionHub'
import { ApiError } from '@/lib/api/client'

// One container for /s/:sessionId. Deep links, refreshes and QR scans all land here
// and self-sort by (state, isHost, hasToken) (docs/08-frontend-design.md#routes,
// docs/09-ux-flows.md#routestaterole-matrix).
export function Session() {
  const params = useParams()
  const sessionId = params.sessionId ?? ''
  const { identity } = useParticipantToken(sessionId)
  const query = useSession(sessionId)
  // A participant with a token gets live updates; the Processing screen advances to
  // Review off the hub without polling. Visitors render from the REST snapshot only.
  useSessionHub(sessionId, identity?.participantToken ?? null)

  if (query.isPending) {
    return <Centered title="Loading..." />
  }

  if (query.isError) {
    const gone = query.error instanceof ApiError && query.error.status === 404
    return gone ? <ExpiredCard /> : <Centered title="Something went wrong." />
  }

  const snapshot = query.data
  const isHost =
    !!identity && snapshot.hostParticipantId === identity.participantId

  switch (snapshot.state) {
    case 'Review':
      return isHost && identity ? (
        <ReviewScreen snapshot={snapshot} token={identity.participantToken} />
      ) : (
        <HoldingCard />
      )
    case 'Processing':
      return isHost ? <ProcessingScreen /> : <HoldingCard />
    case 'Open':
      return isHost ? (
        <OpenHostScreen snapshot={snapshot} />
      ) : (
        <Centered title="The split is open." />
      )
    case 'Finalized':
      return <Centered title="Split locked." />
  }
}

function Centered({ title }: { title: string }) {
  return (
    <main className="mx-auto flex min-h-svh max-w-md flex-col items-center justify-center gap-2 p-6 text-center">
      <h1 className="text-xl font-semibold tracking-tight">{title}</h1>
    </main>
  )
}

// Non-host arriving before the split is open (docs/09-ux-flows.md#routestaterole-matrix).
function HoldingCard() {
  return (
    <main className="mx-auto flex min-h-svh max-w-md flex-col items-center justify-center gap-2 p-6 text-center">
      <h1 className="text-xl font-semibold tracking-tight">
        This split isn&apos;t open yet
      </h1>
      <p className="text-muted-foreground">
        Hang tight - the host is still setting it up.
      </p>
    </main>
  )
}

function ExpiredCard() {
  return (
    <main className="mx-auto flex min-h-svh max-w-md flex-col items-center justify-center gap-4 p-6 text-center">
      <p className="text-muted-foreground">
        This split has expired - bills don&apos;t live long here.
      </p>
      <Button asChild>
        <Link to="/">Start a new split</Link>
      </Button>
    </main>
  )
}
