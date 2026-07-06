import { useEffect, useState } from 'react'
import QRCode from 'qrcode'
import { Check, Copy } from 'lucide-react'

import { Button } from '@/components/ui/button'
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet'

type Props = {
  joinUrl: string
  shortCode: string
  open: boolean
  onOpenChange: (open: boolean) => void
}

// Host share sheet shown on entering Open and reopenable. The QR is rendered
// client-side from joinUrl - no server image generation
// (docs/09-ux-flows.md#5-share-host-sheet-on-entering-open-reopenable,
// docs/08-frontend-design.md#qr).
export function ShareSheet({ joinUrl, shortCode, open, onOpenChange }: Props) {
  const [qr, setQr] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)

  useEffect(() => {
    if (!open) {
      return
    }

    let active = true
    QRCode.toDataURL(joinUrl, { margin: 1, width: 240 })
      .then((url) => {
        if (active) {
          setQr(url)
        }
      })
      .catch(() => {
        if (active) {
          setQr(null)
        }
      })

    return () => {
      active = false
    }
  }, [open, joinUrl])

  async function copyLink() {
    try {
      await navigator.clipboard.writeText(joinUrl)
      setCopied(true)
      window.setTimeout(() => setCopied(false), 2000)
    } catch {
      // Clipboard blocked - the code and QR are still on screen.
    }
  }

  const host = safeHost(joinUrl)

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="bottom">
        <SheetHeader>
          <SheetTitle>Get the table in</SheetTitle>
        </SheetHeader>

        <div className="flex flex-col items-center gap-4">
          {qr ? (
            <img
              src={qr}
              alt="QR code to join the split"
              className="size-56 rounded-md border bg-white p-2"
            />
          ) : (
            <div className="text-muted-foreground flex size-56 items-center justify-center text-sm">
              Preparing QR...
            </div>
          )}

          <p className="text-center text-sm">
            or enter code{' '}
            <span className="font-mono text-base font-semibold tracking-widest">
              {shortCode}
            </span>
            {host ? <> at {host}/join</> : null}
          </p>
        </div>

        <div className="mt-2 grid grid-cols-2 gap-2">
          <Button variant="outline" onClick={() => void copyLink()}>
            {copied ? <Check /> : <Copy />}
            {copied ? 'Copied' : 'Copy link'}
          </Button>
          <Button onClick={() => onOpenChange(false)}>Done</Button>
        </div>
      </SheetContent>
    </Sheet>
  )
}

function safeHost(url: string): string | null {
  try {
    return new URL(url).host
  } catch {
    return null
  }
}
