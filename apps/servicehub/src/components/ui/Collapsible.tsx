import { ChevronDown } from 'lucide-react'
import { useId, useState, type ReactNode } from 'react'
import type { ColumnHelp } from '../../content/columns'
import { InfoTip } from './InfoTip'

/**
 * A titled section the reader can fold away. Open by default so nothing is hidden until they choose.
 * The (i) sits BESIDE the fold button, not inside it: a button cannot hold a button. Folded content stays in the page (hidden), so a
 * find-in-page or a screen reader's headings list never loses it.
 */
export function Collapsible({ title, summary, defaultOpen = true, help, children }: { title: string; summary?: string; defaultOpen?: boolean; help?: ColumnHelp; children: ReactNode }) {
  const [open, setOpen] = useState(defaultOpen)
  const id = useId()
  return (
    <div className="group" data-open={open || undefined}>
      <div className="flex items-center gap-1 text-xs font-semibold uppercase tracking-wide text-[var(--color-text-muted)]">
        <button type="button" aria-expanded={open} aria-controls={id} onClick={() => setOpen((o) => !o)} className="flex cursor-pointer items-center gap-2 text-left uppercase tracking-wide">
          <ChevronDown className={`h-3.5 w-3.5 shrink-0 transition-transform ${open ? '' : '-rotate-90'}`} aria-hidden="true" />
          {title}
          {summary && <span className="ml-1 font-normal normal-case tracking-normal">— {summary}</span>}
        </button>
        {help && <InfoTip help={help} />}
      </div>
      <div id={id} hidden={!open} className="mt-2">{children}</div>
    </div>
  )
}
