import type { ReactNode } from 'react'
import { InfoTip } from '../../components/ui/InfoTip'
import type { ColumnHelp } from '../../content/columns'
import { helpFor } from '../content/info'

/**
 * The small (i) of the 5.0.0 shell (`Tip`). `help` gives the words directly where they already exist (the navigation array's descriptions); `className` recolours it on the dark header. It opens on hover, on keyboard focus and on click, and says what the label beside it means — the words
 * are `content/info.ts`, written once. A label with no entry draws nothing and says so in the console; a test fails on it, so a page
 * cannot ship an (i) that explains nothing.
 */
export function Tip({ label, context, help: given, className }: { label?: string; context?: string; help?: ColumnHelp; className?: string }) {
  const help = given ?? helpFor(label ?? '', context)
  if (!help) {
    if (typeof console !== 'undefined') console.warn(`No (i) text is written for “${label}”${context ? ` (${context})` : ''}: add it to next/content/info.ts`)
    return null
  }
  return <InfoTip hover help={help} className={className} />
}

/** A label followed by its (i), for anything that is not a table column. */
export function Labelled({ label, context, children }: { label: string; context?: string; children?: ReactNode }) {
  return <span className="inline-flex items-center">{children ?? label}<Tip label={label} context={context} /></span>
}

/**
 * A table column header with its (i). Every column of every table in the shell goes through this, so no column can be left unexplained
 * (`tests/web/unit/next/info.test.ts` fails on a raw `<th` in the shell). The label is the text, or `label` when the text is built.
 */
export function Th({ children, label, context, className = 'px-3 py-2 text-left text-xs font-semibold text-[var(--color-text-muted)]' }: {
  children?: ReactNode; label?: string; context?: string; className?: string
}) {
  const text = label ?? (typeof children === 'string' ? children : '')
  return <th scope="col" className={className}><span className="inline-flex items-center">{children}<Tip label={text} context={context} /></span></th>
}

/**
 * A column header that carries no label of its own: a tick box, an expand arrow, or a column of buttons. Its text is for screen readers, and
 * the buttons it holds say what they do themselves, so it takes no (i). Anything with a visible label uses `Th`.
 */
export function ThHidden({ children, className = 'px-3 py-2 text-left text-xs font-semibold text-[var(--color-text-muted)]' }: { children: ReactNode; className?: string }) {
  return <th scope="col" className={className}>{children}</th>
}
