import { useEffect, useRef, type ReactNode } from 'react'
import { X } from 'lucide-react'

const focusable = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

/**
 * The dialog chrome shared by every modal and side panel: a title, a ✕, Esc to close, focus moved
 * in on open and handed back on close. A modal is centred over a scrim; a panel slides in on the
 * right and leaves the page visible behind it.
 */
export function OverlayFrame({
  kind,
  title,
  description,
  onClose,
  children,
}: {
  kind: 'modal' | 'panel'
  title: string
  description?: string
  onClose: () => void
  children: ReactNode
}) {
  const frame = useRef<HTMLDivElement>(null)
  const onCloseRef = useRef(onClose)
  useEffect(() => {
    onCloseRef.current = onClose
  })

  useEffect(() => {
    const before = document.activeElement instanceof HTMLElement ? document.activeElement : null
    frame.current?.focus()

    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        event.stopPropagation()
        onCloseRef.current()
        return
      }
      if (event.key !== 'Tab' || !frame.current) return
      // Keep Tab inside the dialog while it is open.
      const items = [...frame.current.querySelectorAll<HTMLElement>(focusable)]
      if (items.length === 0) {
        event.preventDefault()
        return
      }
      const first = items[0]
      const last = items[items.length - 1]
      const active = document.activeElement
      if (event.shiftKey && (active === first || active === frame.current)) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && active === last) {
        event.preventDefault()
        first.focus()
      }
    }

    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('keydown', onKeyDown)
      before?.focus()
    }
  }, [])

  const isModal = kind === 'modal'
  return (
    <div className={`fixed inset-0 z-40 flex ${isModal ? 'items-center justify-center p-6' : 'justify-end'}`}>
      <div
        className="absolute inset-0 bg-black/30"
        aria-hidden="true"
        data-testid="overlay-scrim"
        onClick={onClose}
      />
      <div
        ref={frame}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        tabIndex={-1}
        data-overlay={kind}
        className={
          isModal
            ? 'relative max-h-full w-full max-w-xl overflow-y-auto rounded-2xl bg-[var(--color-surface)] shadow-xl'
            : 'relative h-full w-full max-w-md overflow-y-auto border-l border-[var(--color-border)] bg-[var(--color-surface)] shadow-xl'
        }
      >
        <div className="flex items-start gap-3 border-b border-[var(--color-border)] px-5 py-4">
          <div className="min-w-0 flex-1">
            <h2 className="text-base font-semibold text-[var(--color-text)]">{title}</h2>
            {description && <p className="mt-0.5 text-sm text-[var(--color-text-muted)]">{description}</p>}
          </div>
          <button
            type="button"
            aria-label="Close"
            onClick={onClose}
            className="rounded-lg p-1.5 text-[var(--color-text-muted)] hover:bg-[var(--color-surface-muted)]"
          >
            <X className="h-4 w-4" />
          </button>
        </div>
        <div className="px-5 py-4">{children}</div>
      </div>
    </div>
  )
}
