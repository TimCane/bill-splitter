import { Loader2 } from 'lucide-react'

import type { HubStatus } from '@/hooks/useSessionHub'
import { cn } from '@/lib/utils'

// Maps the hub status to the header pill: live / reconnecting / offline
// (docs/09-ux-flows.md#7-claim---state-open-the-main-screen-everyone). Offline
// means automatic reconnect gave up, so the only way back is a reload.
export function ConnectionPill({ status }: { status: HubStatus }) {
  if (status === 'disconnected') {
    return (
      <button
        type="button"
        onClick={() => window.location.reload()}
        className={cn(pillClass, 'border-destructive/40 text-destructive')}
      >
        <span className="size-1.5 rounded-full bg-destructive" />
        offline
      </button>
    )
  }

  if (status === 'reconnecting') {
    return (
      <span className={cn(pillClass, 'border-amber-500/40 text-amber-600')}>
        <Loader2 className="size-3 animate-spin" />
        reconnecting
      </span>
    )
  }

  return (
    <span className={cn(pillClass, 'border-green-600/40 text-green-700')}>
      <span className="size-1.5 rounded-full bg-green-600" />
      live
    </span>
  )
}

const pillClass =
  'inline-flex items-center gap-1.5 rounded-full border px-2 py-0.5 text-xs font-medium'
