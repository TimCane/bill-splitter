import { Receipt } from 'lucide-react'

import { Button } from '@/components/ui/button'

export function Landing() {
  return (
    <main className="mx-auto flex min-h-svh max-w-md flex-col items-center justify-center gap-6 p-6 text-center">
      <Receipt className="size-12 text-primary" />
      <div className="space-y-2">
        <h1 className="text-2xl font-semibold tracking-tight">
          Split the bill
        </h1>
        <p className="text-muted-foreground">
          Scan a receipt, claim your items, settle up. Nothing is kept.
        </p>
      </div>
      <Button size="lg">Scan a receipt</Button>
    </main>
  )
}
