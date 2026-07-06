import { useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Minus, Plus, QrCode } from 'lucide-react'

import { ConnectionPill } from '@/components/session/ConnectionPill'
import { NameSheet } from '@/components/session/NameSheet'
import { ShareSheet } from '@/components/session/ShareSheet'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet'
import { useCapabilities } from '@/hooks/useCapabilities'
import type { Identity } from '@/hooks/useParticipantToken'
import { applySnapshot, sessionKey } from '@/hooks/useSession'
import type { SessionHub } from '@/hooks/useSessionHub'
import { finalizeSplit } from '@/lib/api/client'
import type { Item, SessionSnapshot } from '@/lib/api/schemas'
import { emailError, maskEmail, parseEmail } from '@/lib/email'
import { formatMinor } from '@/lib/money'
import { HubError } from '@/lib/realtime/contract'
import { cn } from '@/lib/utils'

type Props = {
  snapshot: SessionSnapshot
  identity: Identity
  isHost: boolean
  hub: SessionHub
  // Called with the masked address when the host finalizes and asked for the
  // summary email, so the summary screen can confirm it (docs/09-ux-flows.md#8).
  onEmailedSummary: (maskedEmail: string) => void
}

// The main screen, everyone, state Open
// (docs/09-ux-flows.md#7-claim---state-open-the-main-screen-everyone). Renders
// straight from the snapshot - every amount here was computed on the server; the
// screen only formats. Gestures go over the hub and are not optimistic: the
// authoritative snapshot lands within the coalescing window.
export function ClaimScreen({
  snapshot,
  identity,
  isHost,
  hub,
  onEmailedSummary,
}: Props) {
  const queryClient = useQueryClient()
  const [renameOpen, setRenameOpen] = useState(false)
  const [everyoneOpen, setEveryoneOpen] = useState(false)
  const [finalizeOpen, setFinalizeOpen] = useState(false)
  const { emailEnabled } = useCapabilities(isHost)
  const waitingForTable = isHost && snapshot.participants.length <= 1
  // The Share sheet greets the host on entering Open while the table is empty;
  // after that it stays reachable from the footer.
  const [shareOpen, setShareOpen] = useState(waitingForTable)

  const me = snapshot.participants.find(
    (p) => p.participantId === identity.participantId,
  )
  const myTotal = snapshot.totals.find(
    (t) => t.participantId === identity.participantId,
  )
  const nameOf = (participantId: string) =>
    snapshot.participants.find((p) => p.participantId === participantId)
      ?.displayName ?? '?'

  // wrong-state means the split finalized under us: refetch and let Session.tsx
  // switch to the summary - a soft refresh, not an error toast
  // (docs/05-realtime-contract.md#hub-errors). Anything else is transient; the
  // next snapshot corrects the UI.
  function gesture(run: () => Promise<void>) {
    run().catch((error: unknown) => {
      if (
        error instanceof HubError &&
        (error.code === 'wrong-state' || error.code === 'session-not-found')
      ) {
        void queryClient.invalidateQueries({
          queryKey: sessionKey(snapshot.sessionId),
        })
      } else {
        console.warn('Claim gesture failed', error)
      }
    })
  }

  return (
    <main className="mx-auto flex min-h-svh max-w-md flex-col">
      <header className="bg-background sticky top-0 z-10 border-b px-4 py-3">
        <div className="flex items-center justify-between gap-2">
          <h1 className="text-base font-semibold tracking-tight">
            Split {snapshot.shortCode}
          </h1>
          <ConnectionPill status={hub.status} />
        </div>
        <div className="mt-1 flex items-center justify-between gap-2 text-sm">
          <button
            type="button"
            className="text-muted-foreground underline-offset-4 hover:underline"
            onClick={() => setRenameOpen(true)}
          >
            You: {me?.displayName ?? '?'}
          </button>
          {myTotal ? (
            <span className="font-medium">
              Your {formatMinor(myTotal.totalMinor, snapshot.currency)}
            </span>
          ) : null}
        </div>
      </header>

      {waitingForTable ? (
        <div className="border-b px-4 py-6 text-center">
          <p className="text-muted-foreground">
            Waiting for people to join - show them the QR.
          </p>
          <Button
            className="mt-3"
            variant="outline"
            onClick={() => setShareOpen(true)}
          >
            <QrCode />
            Show QR
          </Button>
        </div>
      ) : null}

      <ul className="flex-1">
        {snapshot.items.map((item) => (
          <ClaimRow
            key={item.itemId}
            item={item}
            currency={snapshot.currency}
            meId={identity.participantId}
            nameOf={nameOf}
            onToggle={(claimed) =>
              gesture(() =>
                claimed
                  ? hub.unclaimItem(item.itemId)
                  : hub.claimItem(item.itemId),
              )
            }
            onSetShares={(shares) =>
              gesture(() => hub.setShares(item.itemId, shares))
            }
          />
        ))}
      </ul>

      <footer className="bg-background sticky bottom-0 border-t px-4 py-3">
        <p className="text-muted-foreground text-sm">
          Unclaimed{' '}
          {formatMinor(snapshot.unclaimedTotalMinor, snapshot.currency)} of{' '}
          {formatMinor(snapshot.bill.totalMinor, snapshot.currency)}
        </p>
        <div className="mt-2 flex items-center gap-2">
          <Button
            variant="outline"
            className="flex-1"
            onClick={() => setEveryoneOpen(true)}
          >
            Everyone
          </Button>
          {isHost ? (
            <>
              <Button className="flex-1" onClick={() => setFinalizeOpen(true)}>
                Finalize
              </Button>
              <Button
                variant="ghost"
                size="icon"
                aria-label="Show QR"
                onClick={() => setShareOpen(true)}
              >
                <QrCode />
              </Button>
            </>
          ) : null}
        </div>
      </footer>

      <NameSheet
        sessionId={snapshot.sessionId}
        token={identity.participantToken}
        currentName={me?.displayName ?? ''}
        open={renameOpen}
        onOpenChange={setRenameOpen}
        onSaved={(next) => applySnapshot(queryClient, next)}
      />

      <EveryoneSheet
        snapshot={snapshot}
        nameOf={nameOf}
        open={everyoneOpen}
        onOpenChange={setEveryoneOpen}
      />

      {isHost && snapshot.joinUrl && snapshot.shortCode ? (
        <ShareSheet
          joinUrl={snapshot.joinUrl}
          shortCode={snapshot.shortCode}
          open={shareOpen}
          onOpenChange={setShareOpen}
        />
      ) : null}

      {isHost ? (
        <FinalizeDialog
          snapshot={snapshot}
          token={identity.participantToken}
          emailEnabled={emailEnabled}
          open={finalizeOpen}
          onOpenChange={setFinalizeOpen}
          onFinalized={(next) => applySnapshot(queryClient, next)}
          onEmailedSummary={onEmailedSummary}
        />
      ) : null}
    </main>
  )
}

type FinalizeDialogProps = {
  snapshot: SessionSnapshot
  token: string
  emailEnabled: boolean
  open: boolean
  onOpenChange: (open: boolean) => void
  onFinalized: (snapshot: SessionSnapshot) => void
  onEmailedSummary: (maskedEmail: string) => void
}

// Host-only lock confirmation (docs/09-ux-flows.md#7). The email field only shows
// when the server can send (the capability flag); the masked confirmation is
// derived here and handed up before the summary screen takes over.
function FinalizeDialog({
  snapshot,
  token,
  emailEnabled,
  open,
  onOpenChange,
  onFinalized,
  onEmailedSummary,
}: FinalizeDialogProps) {
  const [email, setEmail] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [finalizing, setFinalizing] = useState(false)

  async function confirm() {
    let address: string | null = null
    if (emailEnabled && email.trim().length > 0) {
      address = parseEmail(email)
      if (address === null) {
        setError(emailError)
        return
      }
    }

    setFinalizing(true)
    setError(null)
    try {
      const next = await finalizeSplit(snapshot.sessionId, token, address)
      if (address) {
        onEmailedSummary(maskEmail(address))
      }

      // The finalized snapshot flips the route to the summary; this component
      // unmounts with it, so there is no busy state to reset on success.
      onFinalized(next)
    } catch {
      setError('Could not finalize. Try again.')
      setFinalizing(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Lock the split?</DialogTitle>
          <DialogDescription>
            Unclaimed{' '}
            {formatMinor(snapshot.unclaimedTotalMinor, snapshot.currency)} gets
            split between everyone.
          </DialogDescription>
        </DialogHeader>

        {emailEnabled ? (
          <div className="flex flex-col gap-2">
            <Label htmlFor="summary-email">
              Email me the summary (optional)
            </Label>
            <Input
              id="summary-email"
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="you@example.com"
            />
            <p className="text-muted-foreground text-sm">
              Deleted after sending. The split stays viewable for 1 hour.
            </p>
          </div>
        ) : null}

        {error ? <p className="text-destructive text-sm">{error}</p> : null}

        <DialogFooter>
          <Button
            variant="outline"
            onClick={() => onOpenChange(false)}
            disabled={finalizing}
          >
            Cancel
          </Button>
          <Button onClick={() => void confirm()} disabled={finalizing}>
            Lock the split
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

type ClaimRowProps = {
  item: Item
  currency: string
  meId: string
  nameOf: (participantId: string) => string
  onToggle: (claimed: boolean) => void
  onSetShares: (shares: number) => void
}

function ClaimRow({
  item,
  currency,
  meId,
  nameOf,
  onToggle,
  onSetShares,
}: ClaimRowProps) {
  const myClaim = item.claims.find((c) => c.participantId === meId)
  const quantityPrefix = item.quantity > 1 ? `${item.quantity}x ` : ''

  // SetShares is absolute, and the authoritative snapshot lags a round-trip
  // plus the coalescing window - so steps accumulate from the last sent value,
  // not the rendered one, or two quick taps would both send the same weight.
  // The pending value expires quickly in case a send was dropped.
  const sent = useRef<{ shares: number; at: number } | null>(null)

  useEffect(() => {
    if (!myClaim || myClaim.shares === sent.current?.shares) {
      sent.current = null
    }
  }, [myClaim])

  function step(delta: number) {
    if (!myClaim) {
      return
    }

    const now = Date.now()
    const pending =
      sent.current && now - sent.current.at < 2000 ? sent.current.shares : null
    const next = Math.min(99, Math.max(1, (pending ?? myClaim.shares) + delta))
    sent.current = { shares: next, at: now }
    onSetShares(next)
  }

  return (
    <li className="border-b px-4 py-3">
      <div className="flex items-baseline justify-between gap-2">
        <span className="font-medium">
          {quantityPrefix}
          {item.name}
        </span>
        <span>{formatMinor(item.priceMinor, currency)}</span>
      </div>

      <div className="mt-2 flex items-center justify-between gap-2">
        <Button
          size="sm"
          variant={myClaim ? 'default' : 'outline'}
          aria-pressed={!!myClaim}
          onClick={() => onToggle(!!myClaim)}
        >
          {myClaim ? 'Claimed' : 'Mine'}
        </Button>
        {item.claims.length > 0 ? (
          <span className="flex items-center gap-1">
            {item.claims.map((claim) => (
              <span
                key={claim.participantId}
                title={nameOf(claim.participantId)}
                className="bg-secondary text-secondary-foreground inline-flex size-6 items-center justify-center rounded-full text-xs font-medium"
              >
                {initialsOf(nameOf(claim.participantId))}
              </span>
            ))}
          </span>
        ) : (
          <span className="text-muted-foreground text-sm">unclaimed</span>
        )}
      </div>

      {myClaim ? (
        <div className="mt-2 flex items-center justify-between gap-2 text-sm">
          <YourShare amountMinor={myClaim.allocatedMinor} currency={currency} />
          <div className="flex items-center gap-2">
            <Button
              size="icon"
              variant="outline"
              aria-label="Decrease shares"
              disabled={myClaim.shares <= 1}
              onClick={() => step(-1)}
            >
              <Minus />
            </Button>
            <span className="w-6 text-center tabular-nums" aria-label="Shares">
              {myClaim.shares}
            </span>
            <Button
              size="icon"
              variant="outline"
              aria-label="Increase shares"
              disabled={myClaim.shares >= 99}
              onClick={() => step(1)}
            >
              <Plus />
            </Button>
          </div>
        </div>
      ) : null}
    </li>
  )
}

// A retroactive change (someone joining or leaving an item I claimed) must read
// as intentional, so the amount flashes when it moves (docs/00-overview.md#known-risks).
function YourShare({
  amountMinor,
  currency,
}: {
  amountMinor: number
  currency: string
}) {
  const [highlighted, setHighlighted] = useState(false)
  const previous = useRef(amountMinor)

  useEffect(() => {
    if (previous.current === amountMinor) {
      return
    }

    previous.current = amountMinor
    setHighlighted(true)
    const timer = setTimeout(() => setHighlighted(false), 800)
    return () => clearTimeout(timer)
  }, [amountMinor])

  return (
    <span
      className={cn(
        'rounded px-1 transition-colors duration-700',
        highlighted && 'bg-primary/15',
      )}
    >
      your share {formatMinor(amountMinor, currency)}
    </span>
  )
}

type EveryoneSheetProps = {
  snapshot: SessionSnapshot
  nameOf: (participantId: string) => string
  open: boolean
  onOpenChange: (open: boolean) => void
}

// Per-person totals straight from snapshot.totals; every amount is a
// server-allocated component rendered as-is - the sheet formats, never sums
// (CLAUDE.md: the frontend never calculates).
function EveryoneSheet({
  snapshot,
  nameOf,
  open,
  onOpenChange,
}: EveryoneSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="bottom">
        <SheetHeader>
          <SheetTitle>Everyone</SheetTitle>
        </SheetHeader>
        <ul className="flex flex-col gap-2 pb-4">
          {snapshot.totals.map((total) => (
            <li
              key={total.participantId}
              className="flex items-baseline justify-between gap-2"
            >
              <span className="font-medium">{nameOf(total.participantId)}</span>
              <span className="text-muted-foreground text-sm">
                items {formatMinor(total.itemsMinor, snapshot.currency)}
                {' / '}
                tax {formatMinor(total.taxMinor, snapshot.currency)}
                {' / '}
                tip {formatMinor(total.tipMinor, snapshot.currency)}
                {' / '}
                service {formatMinor(total.serviceMinor, snapshot.currency)}
              </span>
              <span className="font-medium">
                {formatMinor(total.totalMinor, snapshot.currency)}
              </span>
            </li>
          ))}
          <li className="text-muted-foreground flex items-baseline justify-between border-t pt-2 text-sm">
            <span>Unclaimed</span>
            <span>
              {formatMinor(snapshot.unclaimedTotalMinor, snapshot.currency)}
            </span>
          </li>
        </ul>
      </SheetContent>
    </Sheet>
  )
}

function initialsOf(name: string): string {
  return name
    .trim()
    .split(/\s+/)
    .slice(0, 2)
    .map((word) => (word[0] ?? '').toUpperCase())
    .join('')
}
