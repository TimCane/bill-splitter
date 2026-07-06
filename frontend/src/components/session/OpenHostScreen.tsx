import { useState } from 'react'
import { QrCode } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { ShareSheet } from '@/components/session/ShareSheet'
import type { SessionSnapshot } from '@/lib/api/schemas'

// Host view once the split is Open. The full Claim screen is M5; for now this is
// the Share entry point - the sheet opens on arrival and reopens from here
// (docs/09-ux-flows.md#5-share-host-sheet-on-entering-open-reopenable).
export function OpenHostScreen({ snapshot }: { snapshot: SessionSnapshot }) {
  const [shareOpen, setShareOpen] = useState(true)

  return (
    <main className="mx-auto flex min-h-svh max-w-md flex-col items-center justify-center gap-4 p-6 text-center">
      <h1 className="text-xl font-semibold tracking-tight">
        Split {snapshot.shortCode}
      </h1>
      <p className="text-muted-foreground">
        Waiting for people to join - show them the QR.
      </p>
      <Button onClick={() => setShareOpen(true)}>
        <QrCode />
        Show QR
      </Button>

      {snapshot.joinUrl && snapshot.shortCode ? (
        <ShareSheet
          joinUrl={snapshot.joinUrl}
          shortCode={snapshot.shortCode}
          open={shareOpen}
          onOpenChange={setShareOpen}
        />
      ) : null}
    </main>
  )
}
