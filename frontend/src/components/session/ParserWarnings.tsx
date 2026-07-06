import { useState } from 'react'
import { X } from 'lucide-react'

// Dismissible rows under the checksum banner, one per parser discard line
// (docs/09-ux-flows.md#4-review-host-gate---state-review). Copy is verbatim.
export function ParserWarnings({ warnings }: { warnings: string[] }) {
  const [dismissed, setDismissed] = useState<ReadonlySet<number>>(new Set())

  const visible = warnings
    .map((text, index) => ({ text, index }))
    .filter(({ index }) => !dismissed.has(index))

  if (visible.length === 0) {
    return null
  }

  return (
    <div className="flex flex-col gap-1">
      {visible.map(({ text, index }) => (
        <div
          key={index}
          className="text-muted-foreground flex items-center justify-between gap-2 rounded-md border border-dashed px-3 py-2 text-sm"
        >
          <span>We skipped a line that looked like &ldquo;{text}&rdquo;.</span>
          <button
            type="button"
            aria-label="Dismiss"
            className="hover:text-foreground shrink-0"
            onClick={() => setDismissed((prev) => new Set(prev).add(index))}
          >
            <X className="size-4" />
          </button>
        </div>
      ))}
    </div>
  )
}
