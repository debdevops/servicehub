import { ChevronDown } from 'lucide-react'
import type { ReactNode } from 'react'

/** A titled section the reader can fold away. Open by default so nothing is hidden until they choose. */
export function Collapsible({ title, summary, defaultOpen = true, children }: { title: string; summary?: string; defaultOpen?: boolean; children: ReactNode }) {
  return (
    <details open={defaultOpen} className="group">
      <summary className="flex cursor-pointer list-none items-center gap-2 text-xs font-semibold uppercase tracking-wide text-[var(--color-text-muted)] [&::-webkit-details-marker]:hidden">
        <ChevronDown className="h-3.5 w-3.5 -rotate-90 transition-transform group-open:rotate-0" aria-hidden="true" />
        {title}
        {summary && <span className="ml-1 font-normal normal-case tracking-normal">— {summary}</span>}
      </summary>
      <div className="mt-2">{children}</div>
    </details>
  )
}
