import { Maximize2, Minimize2 } from 'lucide-react'
import { useState, type ReactNode } from 'react'
import { OverlayFrame } from '../../components/overlays/OverlayFrame'

/** The ⤢ / ⤡ button in a details window's header: expand the side window to a modal, or bring it back. */
export function ExpandButton({ expanded, onToggle }: { expanded: boolean; onToggle: () => void }) {
  return (
    <button type="button" aria-label={expanded ? 'Back to the side window' : 'Expand to a full window'} title={expanded ? 'Back to the side window' : 'Expand'}
      onClick={onToggle} className="rounded-lg p-1.5 text-[var(--color-text-muted)] hover:bg-[var(--color-surface-muted)]">
      {expanded ? <Minimize2 className="h-4 w-4" aria-hidden="true" /> : <Maximize2 className="h-4 w-4" aria-hidden="true" />}
    </button>
  )
}

/**
 * How details look everywhere in 5.0.0: they open in a **side window** beside the page (the page stays visible and clickable), and the
 * expand button turns the same window into a **modal** for careful reading. Close (✕ or Esc) removes it. Every screen that shows one
 * record in full uses this, so a person learns it once. Use it for a page's own selection; a URL-driven overlay (the message, a hand-off)
 * gets the same behaviour from the overlay host (`nextOverlayDetails`).
 */
export function DetailWindow({ title, description, onClose, children }: { title: string; description?: string; onClose: () => void; children: ReactNode }) {
  const [expanded, setExpanded] = useState(false)
  return (
    <OverlayFrame
      kind={expanded ? 'modal' : 'panel'}
      size={expanded ? 'wide' : 'drawer'}
      docked={!expanded}
      title={title}
      description={description}
      onClose={onClose}
      actions={<ExpandButton expanded={expanded} onToggle={() => setExpanded((v) => !v)} />}
    >
      {children}
    </OverlayFrame>
  )
}
