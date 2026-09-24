import { Link, NavLink, Outlet, useLocation } from 'react-router-dom'
import { entriesInGroup, hrefOf, type NavEntry } from '../nav/navigation'

/**
 * The application frame: a 56px header, one 260px sidebar, content, and a footer.
 *
 * One sidebar, deliberately. 4.0.0 had an icon rail, a Quick Access panel, a Namespaces panel and a
 * workspace toolbar — four navigation surfaces that drifted apart from each other.
 *
 * Every entry here comes from the navigation array (ARCHITECTURE §4.5). Nothing is hard-coded, so
 * the sidebar and the router cannot disagree.
 */
export function AppLayout() {
  return (
    <div className="min-h-screen">
      <header
        className="sticky top-0 z-20 flex items-center gap-4 border-b border-[var(--color-border)] bg-[var(--color-surface)] px-6"
        style={{ height: 'var(--header-height)' }}
      >
        <span className="text-[15px] font-semibold tracking-tight text-[var(--color-text)]">ServiceHub</span>
        <span className="rounded-full bg-[var(--color-surface-muted)] px-2 py-0.5 text-xs text-[var(--color-text-muted)]">
          {import.meta.env.VITE_APP_VERSION}
        </span>
      </header>

      <div className="flex">
        <nav
          aria-label="Main"
          className="sticky shrink-0 overflow-y-auto border-r border-[var(--color-border)] bg-[var(--color-surface)] px-3 py-4"
          style={{
            width: 'var(--sidebar-width)',
            top: 'var(--header-height)',
            height: 'calc(100vh - var(--header-height))',
          }}
        >
          <NavSection entries={entriesInGroup('primary')} />
          <NavSection label="Work" entries={entriesInGroup('work')} />
          <NavSection label="Clouds" entries={entriesInGroup('clouds')} />
          <NavSection entries={entriesInGroup('utility')} />
        </nav>

        <main className="min-w-0 flex-1" style={{ maxWidth: 'var(--content-max-width)' }}>
          <Outlet />
        </main>
      </div>
    </div>
  )
}

const itemClass = 'mb-0.5 flex items-center gap-2.5 rounded-lg px-3 py-2 text-sm transition-colors'
const idleClass = 'text-[var(--color-text)] hover:bg-[var(--color-surface-muted)]'
const activeClass = 'bg-[var(--color-primary-50)] font-medium text-[var(--color-primary-700)]'

function NavSection({ label, entries }: { label?: string; entries: readonly NavEntry[] }) {
  const { pathname } = useLocation()
  if (entries.length === 0) return null

  return (
    <div className="mb-6">
      {label && (
        <h2 className="px-3 pb-2 text-[11px] font-semibold uppercase tracking-wide text-[var(--color-text-muted)]">
          {label}
        </h2>
      )}
      <ul>
        {entries.map((entry) => (
          <li key={entry.id}>
            {entry.kind === 'page' ? (
              <NavLink
                to={entry.path}
                end={entry.path === '/'}
                title={entry.description}
                className={({ isActive }) => [itemClass, isActive ? activeClass : idleClass].join(' ')}
              >
                <entry.icon className="h-4 w-4 shrink-0" />
                {entry.label}
              </NavLink>
            ) : (
              // Tabs, panels and modals are URL states on a page, not routes (D45).
              <Link to={hrefOf(entry, pathname)} title={entry.description} className={[itemClass, idleClass].join(' ')}>
                <entry.icon className="h-4 w-4 shrink-0" />
                {entry.label}
              </Link>
            )}
          </li>
        ))}
      </ul>
    </div>
  )
}
