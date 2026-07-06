import { Loader2 } from 'lucide-react'

// Host wait screen while OCR runs. Live via the hub - no polling, no button; a
// Failed parse drops into Review with the amber banner, so there is no dead end
// (docs/09-ux-flows.md#3-processing---host-at-sid-state-processing).
export function ProcessingScreen() {
  return (
    <main className="mx-auto flex min-h-svh max-w-md flex-col items-center justify-center gap-3 p-6 text-center">
      <Loader2 className="text-primary size-8 animate-spin" />
      <h1 className="text-xl font-semibold tracking-tight">
        Reading your receipt...
      </h1>
      <p className="text-muted-foreground">Usually takes a few seconds.</p>
    </main>
  )
}
