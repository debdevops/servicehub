import { Link } from 'react-router-dom'
import { componentTabs } from '../../nav/navigation'
import { Tip } from '../Tip'

/**
 * The tabs of one connection's page. A tab is a view of this page (`?tab=`), not a place of its own, so each is a link and the URL
 * carries which one is open. Only the tabs this component has appear: "Stuck messages" is listed only where the catalogue has a
 * stuck row for the component.
 */
export function DetailTabs({ basePath, active, hasStuck, deadLetterCount }: { basePath: string; active: string; hasStuck: boolean; deadLetterCount: string }) {
  const tabs = componentTabs.filter((t) => t.value !== 'stuck' || hasStuck)
  return (
    <nav aria-label="Sections of this connection" className="flex flex-wrap items-center gap-1 border-b border-[var(--color-border)]">
      {tabs.map((t) => {
        const on = t.value === active
        return (
          <Link
            key={t.value}
            to={t.value === 'overview' ? basePath : `${basePath}?tab=${t.value}`}
            aria-current={on ? 'page' : undefined}
            className={`-mb-px border-b-2 px-3 py-2.5 text-sm font-semibold ${on ? 'border-[var(--color-primary-600)] text-[var(--color-primary-700)]' : 'border-transparent text-[var(--color-text-muted)] hover:text-[var(--color-text)]'}`}
          >
            {t.label}{t.value === 'dead-letters' && ` (${deadLetterCount})`}
          </Link>
        )
      })}
      <Tip label="Tabs" />
    </nav>
  )
}
