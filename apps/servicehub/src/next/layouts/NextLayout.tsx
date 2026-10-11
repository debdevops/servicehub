import { Link, NavLink, Outlet, useParams } from 'react-router-dom'
import { onRequestComponents, sidebarComponents, useCoverage } from '../../hooks/useCoverage'
import { cloudLabel, cloudOf, cloudScopes, parseScope, type CloudScope } from '../lib/clouds'
import { NEXT_ROOT, nextEntriesIn, nextEntry, nextHref, type NextPage } from '../nav/navigation'

const linkBase = 'flex h-9 items-center gap-2.5 rounded-lg px-2.5 text-sm text-[var(--color-text)] hover:bg-[var(--color-surface-muted)]'
const linkOn = 'bg-[var(--color-primary-50)] font-semibold text-[var(--color-primary-700)]'
const groupTitle = 'mx-2.5 mb-1 mt-4 text-xs font-bold text-[var(--color-text)]'

function MenuLink({ to, end, children }: { to: string; end?: boolean; children: React.ReactNode }) {
  return (
    <NavLink to={to} end={end} className={({ isActive }) => `${linkBase} ${isActive ? linkOn : ''}`}>
      {children}
    </NavLink>
  )
}

/**
 * The 5.0.0 shell (design: every `*-home` page; build-plan unit A3): header, the one left menu, and the page.
 *
 * The menu is built from two lists and nothing else: the navigation array (Home, the tools, the footer links) and the coverage
 * catalogue (the PaaS Components and "Later" sections). Nothing here names a component or a tool by hand.
 *
 * NOT YET HERE, each with its unit: the Agent's state and the Emergency stop button in the header (F5), the caller's role (C1),
 * the badges on "Needs your attention" and "Pending approvals" (B2), the cloud's own header colour (C1), and the overlay
 * host for `?dialog=` and `?panel=` (F1). They are left out rather than faked.
 */
export function NextLayout() {
  const params = useParams()
  // `configuration` and `coverage` are not clouds: on those pages the menu shows every cloud's components.
  const scope: CloudScope = parseScope(params.cloud)
  const cloud = cloudOf(scope)
  const { data } = useCoverage()
  const rows = data?.rows ?? []
  const home = scope === 'all' ? NEXT_ROOT : nextHref(nextEntry('home'), { cloud: scope })
  const tools = nextEntriesIn('tool') as readonly NextPage[]
  const reference = nextEntriesIn('reference') as readonly NextPage[]

  return (
    <div className="min-h-screen bg-[var(--color-surface-muted)]">
      <header className="flex h-[var(--header-height)] items-center gap-4 bg-[var(--color-primary-900)] px-5 text-white">
        <span className="text-lg font-bold">ServiceHub</span>
        <span className="text-sm font-semibold">{cloudLabel[scope]}</span>
        <span className="ml-auto rounded-md bg-white/15 px-2.5 py-1 text-xs font-semibold">5.0.0 layout · preview</span>
        <Link to="/" className="rounded-md bg-white/15 px-2.5 py-1 text-xs font-semibold hover:bg-white/25">Back to 4.2.0</Link>
      </header>

      <div className="flex">
        <nav aria-label="ServiceHub" className="w-56 shrink-0 self-stretch border-r border-[var(--color-border)] bg-[var(--color-surface)] px-3 pb-6 pt-3">
          <ul aria-label="Cloud" className="mb-2 flex flex-wrap gap-1">
            {cloudScopes.map((s) => (
              <li key={s}>
                <Link
                  to={s === 'all' ? NEXT_ROOT : nextHref(nextEntry('home'), { cloud: s })}
                  aria-current={s === scope ? 'true' : undefined}
                  className={`block rounded-md border px-2 py-1 text-xs font-semibold ${s === scope ? 'border-[var(--color-primary-600)] bg-[var(--color-primary-50)] text-[var(--color-primary-700)]' : 'border-[var(--color-border)] text-[var(--color-text)]'}`}
                >
                  {cloudLabel[s]}
                </Link>
              </li>
            ))}
          </ul>

          <MenuLink to={home} end>Home</MenuLink>

          <h2 className={groupTitle}>PaaS Components</h2>
          <ul>
            {sidebarComponents(rows, cloud).map((row) => (
              <li key={row.componentKey}>
                <MenuLink to={nextHref(nextEntry('component-list'), { cloud: row.cloud, component: row.componentKey })}>
                  {row.name}
                  {row.status === 'beingValidated' && <span className="ml-auto text-[11px] text-[var(--color-warning)]">Not yet validated</span>}
                </MenuLink>
              </li>
            ))}
          </ul>

          <h2 className={groupTitle}>Later (On request)</h2>
          <ul>
            {onRequestComponents(rows, cloud).map((row) => (
              <li key={row.componentKey} className={`${linkBase} text-[var(--color-text-muted)] hover:bg-transparent`}>
                {row.name}
                <span className="ml-auto rounded border border-[var(--color-border)] px-1.5 text-[11px]">On request</span>
              </li>
            ))}
          </ul>

          <h2 className={groupTitle}>Operational Tools</h2>
          <ul>
            {tools.map((entry) => (
              <li key={entry.id}>
                <MenuLink to={nextHref(entry, { cloud: scope })}>
                  <entry.icon className="h-4 w-4 shrink-0" />
                  {entry.label}
                </MenuLink>
              </li>
            ))}
          </ul>

          <ul className="mt-4 border-t border-[var(--color-border)] pt-3">
            {reference.map((entry) => (
              <li key={entry.id}>
                <MenuLink to={nextHref(entry)}>
                  <entry.icon className="h-4 w-4 shrink-0" />
                  {entry.label}
                </MenuLink>
              </li>
            ))}
          </ul>
        </nav>

        <main className="min-w-0 flex-1">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
