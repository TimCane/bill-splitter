import { useEffect, useState } from 'react'

import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet'
import { fetchReceiptObjectUrl } from '@/lib/api/client'

type Props = {
  sessionId: string
  token: string
  open: boolean
  onOpenChange: (open: boolean) => void
}

// The receipt photo, opened from the header. The image is host-only and carries a
// bearer token, so it is fetched as a blob and shown from an object URL rather than
// a plain <img src> (docs/04-api-contract.md#get-apiv1sessionssessionidreceipt).
export function ReceiptSheet({ sessionId, token, open, onOpenChange }: Props) {
  const [url, setUrl] = useState<string | null>(null)
  const [error, setError] = useState(false)

  useEffect(() => {
    if (!open) {
      return
    }

    let active = true
    let created: string | null = null
    setUrl(null)
    setError(false)

    fetchReceiptObjectUrl(sessionId, token)
      .then((objectUrl) => {
        if (active) {
          created = objectUrl
          setUrl(objectUrl)
        } else {
          URL.revokeObjectURL(objectUrl)
        }
      })
      .catch(() => {
        if (active) {
          setError(true)
        }
      })

    return () => {
      active = false
      if (created) {
        URL.revokeObjectURL(created)
      }
    }
  }, [open, sessionId, token])

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="bottom">
        <SheetHeader>
          <SheetTitle>Receipt</SheetTitle>
        </SheetHeader>
        {error ? (
          <p className="text-muted-foreground text-sm">
            The receipt photo is no longer available.
          </p>
        ) : url ? (
          <img
            src={url}
            alt="The scanned receipt"
            className="mx-auto max-h-[70svh] rounded-md"
          />
        ) : (
          <p className="text-muted-foreground text-sm">Loading...</p>
        )}
      </SheetContent>
    </Sheet>
  )
}
