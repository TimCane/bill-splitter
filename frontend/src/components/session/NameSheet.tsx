import { useState } from 'react'

import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  Sheet,
  SheetContent,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet'
import { renameMe } from '@/lib/api/client'
import type { SessionSnapshot } from '@/lib/api/schemas'

type Props = {
  sessionId: string
  token: string
  currentName: string
  open: boolean
  onOpenChange: (open: boolean) => void
  onSaved: (snapshot: SessionSnapshot) => void
}

// Set your own display name (docs/04-api-contract.md#put-apiv1sessionssessionidparticipantsme).
export function NameSheet({
  sessionId,
  token,
  currentName,
  open,
  onOpenChange,
  onSaved,
}: Props) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="bottom">
        <NameForm
          key={open ? currentName : 'closed'}
          sessionId={sessionId}
          token={token}
          currentName={currentName}
          onSaved={(snapshot) => {
            onSaved(snapshot)
            onOpenChange(false)
          }}
        />
      </SheetContent>
    </Sheet>
  )
}

function NameForm({
  sessionId,
  token,
  currentName,
  onSaved,
}: Omit<Props, 'open' | 'onOpenChange'>) {
  const [name, setName] = useState(currentName)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  function save() {
    const trimmed = name.trim()
    if (trimmed.length < 1 || trimmed.length > 30) {
      setError('Use 1 to 30 characters.')
      return
    }

    setBusy(true)
    setError(null)
    renameMe(sessionId, token, trimmed)
      .then(onSaved)
      .catch(() => {
        setError('Something went wrong. Try again.')
        setBusy(false)
      })
  }

  return (
    <>
      <SheetHeader>
        <SheetTitle>Your name</SheetTitle>
      </SheetHeader>

      <div className="flex flex-col gap-2">
        <Label htmlFor="display-name">Display name</Label>
        <Input
          id="display-name"
          value={name}
          onChange={(e) => setName(e.target.value)}
          maxLength={30}
          autoFocus
        />
        {error ? <p className="text-destructive text-sm">{error}</p> : null}
      </div>

      <SheetFooter>
        <Button onClick={save} disabled={busy}>
          Save
        </Button>
      </SheetFooter>
    </>
  )
}
