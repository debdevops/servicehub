import { Link, useLocation } from 'react-router-dom'

const tabs = [
  { id: 'dlq', label: 'Dead letters' },
  { id: 'active', label: 'Active' },
  { id: 'replayed', label: 'Replayed' },
] as const

/** The three views of Home's table. Each is `?tab=`, so each is linkable and survives a refresh. */
export function WorkTabs({ current }: { current: string }) {
  const { search } = useLocation()
  const hrefFor = (id: string) => {
    const params = new URLSearchParams(search)
    params.set('tab', id)
    ;['page', 'reason', 'entity', 'q', 'message'].forEach((k) => params.delete(k))
    return `/?${params.toString()}`
  }
  return (
    <nav aria-label="Messages" className="mb-4 flex gap-1 border-b border-[var(--color-border)]">
      {tabs.map((t) => (
        <Link
          key={t.id}
          to={hrefFor(t.id)}
          aria-current={t.id === current ? 'page' : undefined}
          className={`-mb-px border-b-2 px-4 py-2 text-sm ${t.id === current ? 'border-[var(--color-primary-600)] font-semibold text-[var(--color-text)]' : 'border-transparent text-[var(--color-text-muted)] hover:text-[var(--color-text)]'}`}
        >
          {t.label}
        </Link>
      ))}
    </nav>
  )
}
