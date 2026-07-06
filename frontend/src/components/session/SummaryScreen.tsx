import { useEffect, useState } from 'react'
import { Check, ChevronDown } from 'lucide-react'

import type { Item, ParticipantTotal, SessionSnapshot } from '@/lib/api/schemas'
import { formatMinor } from '@/lib/money'
import { cn } from '@/lib/utils'

type Props = {
  snapshot: SessionSnapshot
  // The masked address the host asked their summary be sent to, shown only on
  // their own device; null for everyone else (docs/09-ux-flows.md#8).
  sentEmail?: string | null
}

// State Finalized, read-only for everyone including the host
// (docs/09-ux-flows.md#8-summary---state-finalized). Every amount here is a
// server allocation; the screen only formats and adds the per-person totals up
// for the header - it never runs split math (CLAUDE.md).
export function SummaryScreen({ snapshot, sentEmail }: Props) {
  const { currency, totals, participants, unclaimedTotalMinor } = snapshot
  const grandTotalMinor = totals.reduce(
    (sum, total) => sum + total.totalMinor,
    0,
  )
  const nameOf = (participantId: string) =>
    participants.find((p) => p.participantId === participantId)?.displayName ??
    '?'

  return (
    <main className="mx-auto flex min-h-svh max-w-md flex-col gap-4 p-4">
      <header className="flex items-center justify-between">
        <h1 className="flex items-center gap-2 text-xl font-semibold tracking-tight">
          Split locked
          <Check className="text-primary size-5" />
        </h1>
        <span className="font-medium">
          {formatMinor(grandTotalMinor, currency)}
        </span>
      </header>

      <ul className="flex flex-col">
        {totals.map((total) => (
          <SummaryRow
            key={total.participantId}
            total={total}
            name={nameOf(total.participantId)}
            items={snapshot.items}
            currency={currency}
          />
        ))}
      </ul>

      {unclaimedTotalMinor > 0 ? (
        <p className="text-muted-foreground text-sm">
          Unclaimed {formatMinor(unclaimedTotalMinor, currency)} was split
          between {participants.length} people.
        </p>
      ) : null}

      {sentEmail ? (
        <p className="text-muted-foreground text-sm">
          Summary sent to {sentEmail}
        </p>
      ) : null}

      <Countdown expiresAt={snapshot.expiresAt} />
    </main>
  )
}

type SummaryRowProps = {
  total: ParticipantTotal
  name: string
  items: Item[]
  currency: string
}

function SummaryRow({ total, name, items, currency }: SummaryRowProps) {
  const [open, setOpen] = useState(false)

  return (
    <li className="border-b">
      <button
        type="button"
        className="flex w-full items-center justify-between gap-2 py-3 text-left"
        onClick={() => setOpen((prev) => !prev)}
        aria-expanded={open}
      >
        <span className="flex items-center gap-1 font-medium">
          {name}
          <ChevronDown
            className={cn(
              'text-muted-foreground size-4 transition-transform',
              open && 'rotate-180',
            )}
          />
        </span>
        <span className="font-medium">
          {formatMinor(total.totalMinor, currency)}
        </span>
      </button>

      {open ? (
        <ul className="text-muted-foreground pb-3 text-sm">
          {items.map((item) => {
            const claim = item.claims.find(
              (c) => c.participantId === total.participantId,
            )
            return claim ? (
              <BreakdownRow
                key={item.itemId}
                label={item.name}
                amountMinor={claim.allocatedMinor}
                currency={currency}
              />
            ) : null
          })}
          <ExtraRow
            label="+ tax"
            amountMinor={total.taxMinor}
            currency={currency}
          />
          <ExtraRow
            label="+ tip"
            amountMinor={total.tipMinor}
            currency={currency}
          />
          <ExtraRow
            label="+ service"
            amountMinor={total.serviceMinor}
            currency={currency}
          />
          <ExtraRow
            label="+ unclaimed"
            amountMinor={total.unclaimedMinor}
            currency={currency}
          />
        </ul>
      ) : null}
    </li>
  )
}

function BreakdownRow({
  label,
  amountMinor,
  currency,
}: {
  label: string
  amountMinor: number
  currency: string
}) {
  return (
    <li className="flex justify-between">
      <span>{label}</span>
      <span>{formatMinor(amountMinor, currency)}</span>
    </li>
  )
}

// Extras only appear when the person carries some; a zero line is noise.
function ExtraRow({
  label,
  amountMinor,
  currency,
}: {
  label: string
  amountMinor: number
  currency: string
}) {
  return amountMinor > 0 ? (
    <BreakdownRow label={label} amountMinor={amountMinor} currency={currency} />
  ) : null
}

// The finalized TTL is server configuration; the client only counts down to the
// snapshot's expiresAt, never computing the lifetime itself
// (docs/09-ux-flows.md#8-summary---state-finalized).
function Countdown({ expiresAt }: { expiresAt: string }) {
  const [now, setNow] = useState(() => Date.now())

  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(timer)
  }, [])

  return (
    <p className="text-muted-foreground text-sm">
      {describeRemaining(new Date(expiresAt).getTime() - now)}
    </p>
  )
}

function describeRemaining(remainingMs: number): string {
  if (remainingMs <= 0) {
    return 'This split has expired.'
  }

  const minutes = Math.ceil(remainingMs / 60000)
  return `Gone in ${minutes} min. Screenshot or email it before then.`
}
