import { useState } from 'react'
import { Minus, Plus, Trash2 } from 'lucide-react'

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
import { addItem, deleteItem, updateItem } from '@/lib/api/client'
import type { Item, SessionSnapshot } from '@/lib/api/schemas'
import { inputToMinor, minorToInput } from '@/lib/moneyInput'

type Props = {
  sessionId: string
  token: string
  // The item being edited, or null to add a new one.
  item: Item | null
  open: boolean
  onOpenChange: (open: boolean) => void
  onSaved: (snapshot: SessionSnapshot) => void
}

// Tap-to-edit (and add) sheet: name, quantity stepper, price, and Delete inside
// (docs/09-ux-flows.md#4-review-host-gate---state-review).
export function ItemEditSheet({
  sessionId,
  token,
  item,
  open,
  onOpenChange,
  onSaved,
}: Props) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="bottom">
        {/* Remount the form per item so its fields reset when the target changes. */}
        <ItemEditForm
          key={item?.itemId ?? 'new'}
          sessionId={sessionId}
          token={token}
          item={item}
          onSaved={(snapshot) => {
            onSaved(snapshot)
            onOpenChange(false)
          }}
        />
      </SheetContent>
    </Sheet>
  )
}

function ItemEditForm({
  sessionId,
  token,
  item,
  onSaved,
}: Omit<Props, 'open' | 'onOpenChange'>) {
  const [name, setName] = useState(item?.name ?? '')
  const [quantity, setQuantity] = useState(item?.quantity ?? 1)
  const [price, setPrice] = useState(item ? minorToInput(item.priceMinor) : '')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function run(action: () => Promise<SessionSnapshot>) {
    setBusy(true)
    setError(null)
    try {
      onSaved(await action())
    } catch {
      setError('Something went wrong. Try again.')
      setBusy(false)
    }
  }

  function save() {
    const priceMinor = inputToMinor(price)
    if (name.trim() === '' || priceMinor === null) {
      setError('Enter a name and a price.')
      return
    }

    const body = { name: name.trim(), quantity, priceMinor }
    void run(() =>
      item
        ? updateItem(sessionId, token, item.itemId, body)
        : addItem(sessionId, token, body),
    )
  }

  return (
    <>
      <SheetHeader>
        <SheetTitle>{item ? 'Edit item' : 'Add item'}</SheetTitle>
      </SheetHeader>

      <div className="flex flex-col gap-4">
        <div className="flex flex-col gap-2">
          <Label htmlFor="item-name">Name</Label>
          <Input
            id="item-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            maxLength={80}
            autoFocus
          />
        </div>

        <div className="flex flex-col gap-2">
          <Label>Quantity</Label>
          <div className="flex items-center gap-3">
            <Button
              type="button"
              variant="outline"
              size="icon"
              aria-label="Decrease quantity"
              onClick={() => setQuantity((q) => Math.max(1, q - 1))}
            >
              <Minus />
            </Button>
            <span className="w-8 text-center tabular-nums" aria-live="polite">
              {quantity}
            </span>
            <Button
              type="button"
              variant="outline"
              size="icon"
              aria-label="Increase quantity"
              onClick={() => setQuantity((q) => q + 1)}
            >
              <Plus />
            </Button>
          </div>
        </div>

        <div className="flex flex-col gap-2">
          <Label htmlFor="item-price">Price</Label>
          <Input
            id="item-price"
            inputMode="decimal"
            value={price}
            onChange={(e) => setPrice(e.target.value)}
            placeholder="0.00"
          />
        </div>

        {error ? <p className="text-destructive text-sm">{error}</p> : null}
      </div>

      <SheetFooter>
        <Button onClick={save} disabled={busy}>
          {item ? 'Save' : 'Add item'}
        </Button>
        {item ? (
          <Button
            variant="destructive"
            disabled={busy}
            onClick={() =>
              void run(() => deleteItem(sessionId, token, item.itemId))
            }
          >
            <Trash2 />
            Delete
          </Button>
        ) : null}
      </SheetFooter>
    </>
  )
}
