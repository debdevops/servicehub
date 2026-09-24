import { Link } from 'react-router-dom'
import type { Surface } from '../../nav/navigation'

/**
 * `Simple | Advanced` — one switch, two links. It states which surface the URL is on and offers the
 * other; it holds no state of its own (ADR-0016 D2).
 */
export function SurfaceSwitch({
  surface,
  simpleHref,
  advancedHref,
}: {
  surface: Surface
  simpleHref: string
  advancedHref: string
}) {
  const segment = (id: Surface, label: string, href: string) => {
    const active = surface === id
    return (
      <Link
        to={href}
        aria-current={active ? 'page' : undefined}
        className={`rounded-md px-3 py-1 text-sm ${active ? 'bg-[var(--color-surface)] font-semibold text-[var(--color-text)] shadow-sm' : 'text-[var(--color-text-muted)] hover:text-[var(--color-text)]'}`}
      >
        {label}
      </Link>
    )
  }
  return (
    <nav aria-label="Surface" className="flex gap-0.5 rounded-lg bg-[var(--color-surface-muted)] p-0.5">
      {segment('simple', 'Simple', simpleHref)}
      {segment('advanced', 'Advanced', advancedHref)}
    </nav>
  )
}
