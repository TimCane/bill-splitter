import { useEffect } from 'react'
import { Link } from 'react-router'
import { Receipt } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { pruneStaleIdentities } from '@/hooks/useParticipantToken'

export function Landing() {
  useEffect(() => {
    pruneStaleIdentities(Date.now())
  }, [])

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
      <p className="text-muted-foreground text-sm">
        Joining someone&apos;s split?{' '}
        <Link
          to="/join"
          className="text-foreground underline underline-offset-4"
        >
          Enter code
        </Link>
      </p>
    </main>
  )
}
