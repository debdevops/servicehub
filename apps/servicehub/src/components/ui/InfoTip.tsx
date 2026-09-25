import { useEffect, useId, useLayoutEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { Info } from 'lucide-react'
import type { ColumnHelp } from '../../content/columns'

/**
 * A small (i) that explains the thing beside it, wherever it is placed.
 *
 * It opens on click or keyboard (Enter/Space) — not hover only, so it works on touch and for keyboard users — and closes on Esc,
 * a click elsewhere, or the same button again. The note is drawn in a portal so a table's scroll container can never clip it.
 * It changes nothing and takes no focus away from the page.
 */
export function InfoTip({ help, className = '' }: { help: ColumnHelp; className?: string }) {
  const [open, setOpen] = useState(false)
  const [pos, setPos] = useState<{ top: number; left: number } | null>(null)
  const button = useRef<HTMLButtonElement>(null)
  const id = useId()

  useLayoutEffect(() => {
    if (!open || !button.current) return
    const r = button.current.getBoundingClientRect()
    const width = 288
    setPos({ top: r.bottom + 6, left: Math.min(Math.max(8, r.left - 8), window.innerWidth - width - 8) })
  }, [open])

  useEffect(() => {
    if (!open) return
    const close = (e: Event) => {
      if (e instanceof KeyboardEvent && e.key !== 'Escape') return
      if (e instanceof MouseEvent && (button.current?.contains(e.target as Node) || (e.target as HTMLElement).closest?.(`[data-infotip="${id}"]`))) return
      setOpen(false)
    }
    document.addEventListener('mousedown', close)
    document.addEventListener('keydown', close)
    window.addEventListener('scroll', () => setOpen(false), { once: true, capture: true })
    return () => {
      document.removeEventListener('mousedown', close)
      document.removeEventListener('keydown', close)
    }
  }, [open, id])

  return (
    <>
      <button
        ref={button}
        type="button"
        aria-label={`About ${help.title}`}
        aria-expanded={open}
        aria-controls={open ? id : undefined}
        onClick={(e) => { e.stopPropagation(); setOpen((o) => !o) }}
        className={`ml-1 inline-flex h-4 w-4 shrink-0 items-center justify-center rounded-full align-middle text-[#9ca3af] transition-colors hover:text-[var(--color-primary-600)] focus-visible:text-[var(--color-primary-600)] ${open ? 'text-[var(--color-primary-600)]' : ''} ${className}`}
      >
        <Info className="h-3.5 w-3.5" aria-hidden="true" />
      </button>
      {open && pos && createPortal(
        <div
          id={id}
          data-infotip={id}
          role="note"
          style={{ position: 'fixed', top: pos.top, left: pos.left, width: 288, zIndex: 60 }}
          className="rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-3.5 text-left normal-case tracking-normal shadow-[0_10px_30px_rgba(0,0,0,0.14)]"
        >
          <p className="text-[13px] font-bold text-[var(--color-text)]">{help.title}</p>
          <p className="mt-1 text-[12.5px] font-normal leading-relaxed text-[var(--color-text-muted)]">{help.text}</p>
        </div>,
        document.body,
      )}
    </>
  )
}

/** A header label followed by its (i). For tables that are not built from `DataTable`. */
export function HelpLabel({ children, help }: { children: React.ReactNode; help: ColumnHelp }) {
  return <span className="inline-flex items-center">{children}<InfoTip help={help} /></span>
}
