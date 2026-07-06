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
import { setBill } from '@/lib/api/client'
import type { Bill, SessionSnapshot } from '@/lib/api/schemas'
import { inputToMinor, minorToInput } from '@/lib/moneyInput'

type Props = {
  sessionId: string
  token: string
  currency: string
  bill: Bill
  open: boolean
  onOpenChange: (open: boolean) => void
  onSaved: (snapshot: SessionSnapshot) => void
}

// The extras + printed-total + currency edit sheet reached from the total row
// (docs/09-ux-flows.md#4-review-host-gate---state-review).
export function BillEditSheet({
  sessionId,
  token,
  currency,
  bill,
  open,
  onOpenChange,
  onSaved,
}: Props) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="bottom">
        <BillEditForm
          key={open ? 'open' : 'closed'}
          sessionId={sessionId}
          token={token}
          currency={currency}
          bill={bill}
          onSaved={(snapshot) => {
            onSaved(snapshot)
            onOpenChange(false)
          }}
        />
      </SheetContent>
    </Sheet>
  )
}

function BillEditForm({
  sessionId,
  token,
  currency,
  bill,
  onSaved,
}: Omit<Props, 'open' | 'onOpenChange'>) {
  const [tax, setTax] = useState(minorToInput(bill.taxMinor, currency))
  const [tip, setTip] = useState(minorToInput(bill.tipMinor, currency))
  const [service, setService] = useState(
    minorToInput(bill.serviceMinor, currency),
  )
  const [total, setTotal] = useState(minorToInput(bill.totalMinor, currency))
  const [code, setCode] = useState(currency)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  function save() {
    const nextCurrency = code.trim().toUpperCase()
    const taxMinor = inputToMinor(tax, nextCurrency)
    const tipMinor = inputToMinor(tip, nextCurrency)
    const serviceMinor = inputToMinor(service, nextCurrency)
    const totalMinor = inputToMinor(total, nextCurrency)

    if (
      taxMinor === null ||
      tipMinor === null ||
      serviceMinor === null ||
      totalMinor === null ||
      nextCurrency.length !== 3
    ) {
      setError('Enter valid amounts and a 3-letter currency.')
      return
    }

    setBusy(true)
    setError(null)
    setBill(sessionId, token, {
      taxMinor,
      tipMinor,
      serviceMinor,
      totalMinor,
      currency: nextCurrency,
    })
      .then(onSaved)
      .catch(() => {
        setError(
          'That currency is not recognised, or an amount is out of range.',
        )
        setBusy(false)
      })
  }

  return (
    <>
      <SheetHeader>
        <SheetTitle>Extras and total</SheetTitle>
      </SheetHeader>

      <div className="flex flex-col gap-4">
        <AmountField id="bill-tax" label="Tax" value={tax} onChange={setTax} />
        <AmountField id="bill-tip" label="Tip" value={tip} onChange={setTip} />
        <AmountField
          id="bill-service"
          label="Service"
          value={service}
          onChange={setService}
        />
        <AmountField
          id="bill-total"
          label="Printed total"
          value={total}
          onChange={setTotal}
        />

        <div className="flex flex-col gap-2">
          <Label htmlFor="bill-currency">Currency</Label>
          <Input
            id="bill-currency"
            value={code}
            onChange={(e) => setCode(e.target.value)}
            maxLength={3}
            className="uppercase"
          />
        </div>

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

function AmountField({
  id,
  label,
  value,
  onChange,
}: {
  id: string
  label: string
  value: string
  onChange: (value: string) => void
}) {
  return (
    <div className="flex flex-col gap-2">
      <Label htmlFor={id}>{label}</Label>
      <Input
        id={id}
        inputMode="decimal"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder="0.00"
      />
    </div>
  )
}
