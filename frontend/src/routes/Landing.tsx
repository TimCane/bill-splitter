import { useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { Receipt } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { ApiError, createSession } from '@/lib/api/client'
import { preprocess } from '@/lib/image'
import { storeIdentity } from '@/hooks/useParticipantToken'

// Landing / Capture (docs/09-ux-flows.md#1-landing--capture). Pick a photo, preview
// it, then preprocess + upload with a progress bar; on 202 the host token lands in
// localStorage and we navigate to the session, which shows the Processing screen.
type Phase =
  | { kind: 'idle' }
  | { kind: 'preview'; file: File; url: string }
  | { kind: 'uploading'; url: string; progress: number }

const RATE_LIMITED =
  'Too many sessions from this network - try again in a few minutes.'
const UPLOAD_FAILED =
  "Couldn't start the split. Check your connection and try again."

export function Landing() {
  const navigate = useNavigate()
  const inputRef = useRef<HTMLInputElement>(null)
  const [phase, setPhase] = useState<Phase>({ kind: 'idle' })
  const [error, setError] = useState<string | null>(null)

  function onPick(file: File | undefined) {
    if (!file) {
      return
    }
    setError(null)
    setPhase({ kind: 'preview', file, url: URL.createObjectURL(file) })
  }

  function retake() {
    if (phase.kind !== 'idle') {
      URL.revokeObjectURL(phase.url)
    }
    setError(null)
    setPhase({ kind: 'idle' })
    inputRef.current?.click()
  }

  async function upload() {
    if (phase.kind !== 'preview') {
      return
    }
    const { file, url } = phase
    setError(null)
    setPhase({ kind: 'uploading', url, progress: 0 })
    try {
      const image = await preprocess(file)
      const created = await createSession(image, (fraction) =>
        setPhase((prev) =>
          prev.kind === 'uploading' ? { ...prev, progress: fraction } : prev,
        ),
      )
      URL.revokeObjectURL(url)
      storeIdentity(
        created.sessionId,
        {
          participantId: created.participantId,
          participantToken: created.participantToken,
        },
        Date.now(),
      )
      void navigate(`/s/${created.sessionId}`)
    } catch (err) {
      setError(
        err instanceof ApiError && err.status === 429
          ? RATE_LIMITED
          : UPLOAD_FAILED,
      )
      setPhase({ kind: 'preview', file, url })
    }
  }

  const input = (
    <input
      ref={inputRef}
      type="file"
      accept="image/*"
      capture="environment"
      className="hidden"
      onChange={(e) => onPick(e.target.files?.[0])}
    />
  )

  if (phase.kind !== 'idle') {
    const uploading = phase.kind === 'uploading'
    return (
      <main className="mx-auto flex min-h-svh max-w-md flex-col justify-center gap-6 p-6">
        {input}
        <img
          src={phase.url}
          alt="Receipt preview"
          className="max-h-[60svh] w-full rounded-lg object-contain"
        />
        {uploading ? (
          <div
            className="bg-muted h-2 w-full overflow-hidden rounded-full"
            role="progressbar"
            aria-label="Upload progress"
            aria-valuenow={Math.round(phase.progress * 100)}
            aria-valuemin={0}
            aria-valuemax={100}
          >
            <div
              className="bg-primary h-full transition-[width]"
              style={{ width: `${Math.round(phase.progress * 100)}%` }}
            />
          </div>
        ) : null}
        {error ? (
          <p className="text-destructive text-center text-sm">{error}</p>
        ) : null}
        <div className="flex gap-3">
          <Button
            variant="outline"
            size="lg"
            className="flex-1"
            onClick={retake}
            disabled={uploading}
          >
            Retake
          </Button>
          <Button
            size="lg"
            className="flex-1"
            onClick={() => void upload()}
            disabled={uploading}
          >
            {uploading ? 'Uploading...' : 'Use photo'}
          </Button>
        </div>
      </main>
    )
  }

  return (
    <main className="mx-auto flex min-h-svh max-w-md flex-col items-center justify-center gap-6 p-6 text-center">
      {input}
      <Receipt className="size-12 text-primary" />
      <div className="space-y-2">
        <h1 className="text-2xl font-semibold tracking-tight">
          Split the bill
        </h1>
        <p className="text-muted-foreground">
          Scan a receipt, claim your items, settle up. Nothing is kept.
        </p>
      </div>
      <Button size="lg" onClick={() => inputRef.current?.click()}>
        Scan a receipt
      </Button>
      <p className="text-muted-foreground text-sm">
        Joining someone&apos;s split?{' '}
        <Link
          to="/join"
          className="text-foreground underline underline-offset-4"
        >
          Enter code
        </Link>
      </p>
    </main>
  )
}
