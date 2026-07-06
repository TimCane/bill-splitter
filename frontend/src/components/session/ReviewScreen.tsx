import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { ChevronRight, Image, Pencil, Plus } from 'lucide-react'

import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { BillEditSheet } from '@/components/session/BillEditSheet'
import { ChecksumBanner } from '@/components/session/ChecksumBanner'
import { ItemEditSheet } from '@/components/session/ItemEditSheet'
import { NameSheet } from '@/components/session/NameSheet'
import { ParserWarnings } from '@/components/session/ParserWarnings'
import { ReceiptSheet } from '@/components/session/ReceiptSheet'
import { getSession, openSplit } from '@/lib/api/client'
import type { Item, SessionSnapshot } from '@/lib/api/schemas'
import { formatMinor } from '@/lib/money'
import { applySnapshot } from '@/hooks/useSession'

type Props = {
  snapshot: SessionSnapshot
  token: string
}

type ActiveSheet =
  | { kind: 'none' }
  | { kind: 'item'; item: Item | null }
  | { kind: 'bill' }
  | { kind: 'receipt' }
  | { kind: 'name' }

// The host review gate: correct the parse, then open the split
// (docs/09-ux-flows.md#4-review-host-gate---state-review).
export function ReviewScreen({ snapshot, token }: Props) {
  const queryClient = useQueryClient()
  const [sheet, setSheet] = useState<ActiveSheet>({ kind: 'none' })
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [opening, setOpening] = useState(false)
  const [openError, setOpenError] = useState<string | null>(null)

  const { sessionId, currency, bill, items } = snapshot
  const you = snapshot.participants.find((p) => p.isHost)

  function saved(next: SessionSnapshot) {
    applySnapshot(queryClient, next)
  }

  async function confirmAndOpen() {
    setOpening(true)
    setOpenError(null)
    try {
      await openSplit(sessionId, token)
      applySnapshot(queryClient, await getSession(sessionId))
    } catch {
      setOpenError('Could not open the split. Try again.')
      setOpening(false)
    }
  }

  return (
    <main className="mx-auto flex min-h-svh max-w-md flex-col gap-4 p-4">
      <header className="flex items-center justify-between">
        <h1 className="text-xl font-semibold tracking-tight">
          Check the items
        </h1>
        <Button
          variant="outline"
          size="sm"
          onClick={() => setSheet({ kind: 'receipt' })}
        >
          <Image />
          Photo
        </Button>
      </header>

      <button
        type="button"
        className="text-muted-foreground flex items-center gap-2 text-sm"
        onClick={() => setSheet({ kind: 'name' })}
      >
        You:{' '}
        <span className="text-foreground font-medium">
          {you?.displayName ?? 'Host'}
        </span>
        <Pencil className="size-3.5" />
      </button>

      <ChecksumBanner bill={bill} ocr={snapshot.ocr} currency={currency} />
      <ParserWarnings warnings={snapshot.ocr.warnings} />

      <section className="flex flex-col gap-1">
        <h2 className="text-muted-foreground text-xs font-medium tracking-wide uppercase">
          Items
        </h2>
        {items.length === 0 ? (
          <p className="text-muted-foreground py-2 text-sm">
            No items yet. Add them by hand.
          </p>
        ) : (
          items.map((item) => (
            <button
              key={item.itemId}
              type="button"
              className="hover:bg-accent flex items-center justify-between rounded-md py-2 text-left"
              onClick={() => setSheet({ kind: 'item', item })}
            >
              <span>
                {item.quantity > 1 ? (
                  <span className="text-muted-foreground">
                    {item.quantity}x{' '}
                  </span>
                ) : null}
                {item.name}
              </span>
              <span className="flex items-center gap-1">
                {formatMinor(item.priceMinor, currency)}
                <ChevronRight className="text-muted-foreground size-4" />
              </span>
            </button>
          ))
        )}
        <Button
          variant="ghost"
          className="mt-1 justify-start"
          onClick={() => setSheet({ kind: 'item', item: null })}
        >
          <Plus />
          Add item
        </Button>
      </section>

      <section className="flex flex-col gap-1">
        <h2 className="text-muted-foreground text-xs font-medium tracking-wide uppercase">
          Extras
        </h2>
        <ExtraRow label="Tax" amount={formatMinor(bill.taxMinor, currency)} />
        <ExtraRow label="Tip" amount={formatMinor(bill.tipMinor, currency)} />
        <ExtraRow
          label="Service"
          amount={formatMinor(bill.serviceMinor, currency)}
        />
        <button
          type="button"
          className="hover:bg-accent mt-1 flex items-center justify-between rounded-md py-2 text-left"
          onClick={() => setSheet({ kind: 'bill' })}
        >
          <span className="font-medium">Printed total</span>
          <span className="flex items-center gap-1 font-medium">
            {formatMinor(bill.totalMinor, currency)}
            <ChevronRight className="text-muted-foreground size-4" />
          </span>
        </button>
      </section>

      <Button size="lg" className="mt-2" onClick={() => setConfirmOpen(true)}>
        Open the split
      </Button>

      <ItemEditSheet
        sessionId={sessionId}
        token={token}
        currency={currency}
        item={sheet.kind === 'item' ? sheet.item : null}
        open={sheet.kind === 'item'}
        onOpenChange={(v) =>
          setSheet(v ? { kind: 'item', item: null } : { kind: 'none' })
        }
        onSaved={saved}
      />
      <BillEditSheet
        sessionId={sessionId}
        token={token}
        currency={currency}
        bill={bill}
        open={sheet.kind === 'bill'}
        onOpenChange={(v) => setSheet(v ? { kind: 'bill' } : { kind: 'none' })}
        onSaved={saved}
      />
      <ReceiptSheet
        sessionId={sessionId}
        token={token}
        open={sheet.kind === 'receipt'}
        onOpenChange={(v) =>
          setSheet(v ? { kind: 'receipt' } : { kind: 'none' })
        }
      />
      <NameSheet
        sessionId={sessionId}
        token={token}
        currentName={you?.displayName ?? 'Host'}
        open={sheet.kind === 'name'}
        onOpenChange={(v) => setSheet(v ? { kind: 'name' } : { kind: 'none' })}
        onSaved={saved}
      />

      <Dialog open={confirmOpen} onOpenChange={setConfirmOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Open the split?</DialogTitle>
            <DialogDescription>
              Ready? Once open, items can&apos;t be edited and the receipt photo
              is deleted.
            </DialogDescription>
          </DialogHeader>
          {openError ? (
            <p className="text-destructive text-sm">{openError}</p>
          ) : null}
          <DialogFooter>
            <Button
              variant="outline"
              onClick={() => setConfirmOpen(false)}
              disabled={opening}
            >
              Cancel
            </Button>
            <Button onClick={() => void confirmAndOpen()} disabled={opening}>
              Open the split
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </main>
  )
}

function ExtraRow({ label, amount }: { label: string; amount: string }) {
  return (
    <div className="flex items-center justify-between py-1 text-sm">
      <span className="text-muted-foreground">{label}</span>
      <span>{amount}</span>
    </div>
  )
}
