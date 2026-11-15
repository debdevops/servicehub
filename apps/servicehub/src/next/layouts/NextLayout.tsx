import { useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { Link, NavLink, Outlet, useParams } from 'react-router-dom'
import { sidebarComponents, useCoverage } from '../../hooks/useCoverage'
import { useEnabledComponents, useHiddenComponents } from '../../hooks/useComponentEnablement'
import { useComponents } from '../../hooks/useComponents'
import { useNamespaces } from '../../hooks/useNamespaces'
import { DemoBanner } from '../../components/banners/DemoEntry'
import { isDemo } from '../../lib/demo/state'
import { NotFoundPage } from '../../pages/NotFoundPage'
import { HeaderStatus } from '../components/HeaderStatus'
import { OverlayHost } from '../components/OverlayHost'
import { StopBand } from '../components/StopBand'
import { PausedBand } from '../components/PausedBand'
import { useNextShortcuts } from '../lib/useNextShortcuts'
import { cloudHeaderClass, cloudLabel, cloudOf, cloudScopes, parseScope, type CloudScope } from '../lib/clouds'
import { NEXT_HOME, nextEntriesIn, nextEntry, nextHref, type NextPage } from '../nav/navigation'
import type { ColumnHelp } from '../../content/columns'
import { Tip } from '../components/Tip'

const linkBase = 'flex min-h-9 items-center py-1 gap-2.5 rounded-lg px-2.5 text-sm text-[var(--color-text)] hover:bg-[var(--color-surface-muted)]'
const linkOn = 'bg-[var(--color-primary-50)] font-semibold text-[var(--color-primary-700)]'
const groupTitle = 'mx-2.5 mb-1 mt-4 text-xs font-bold text-[var(--color-text)]'

/**
 * A menu link that explains itself without an icon: hovering the row (after a short pause) or putting keyboard focus on it opens the same
 * note an (i) would, and Escape closes it. The words are always on the link for a screen reader (`aria-description`, so the page text is not repeated), so nothing is lost
 * by leaving the icon off; 16 fewer icons on every page, and the rows have their width back.
 */
function MenuLink({ to, end, children, help }: { to: string; end?: boolean; children: React.ReactNode; help?: ColumnHelp }) {
  const row = useRef<HTMLDivElement>(null)
  const timer = useRef<ReturnType<typeof setTimeout>>(undefined)
  const [pos, setPos] = useState<{ top: number; left: number } | null>(null)
  const show = (delay: number) => {
    clearTimeout(timer.current)
    timer.current = setTimeout(() => {
      const r = row.current?.getBoundingClientRect()
      if (r) setPos({ top: r.top + r.height / 2 - 24, left: r.right + 8 })
    }, delay)
  }
  const hide = () => { clearTimeout(timer.current); setPos(null) }
  useEffect(() => () => clearTimeout(timer.current), [])
  useEffect(() => {
    if (!pos) return
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setPos(null) }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [pos])

  return (
    <div ref={row} onMouseEnter={() => show(350)} onMouseLeave={hide} onFocus={() => show(0)} onBlur={hide}>
      <NavLink to={to} end={end} aria-description={help?.text} className={({ isActive }) => `${linkBase} min-w-0 flex-1 ${isActive ? linkOn : ''}`}>
        {children}
      </NavLink>
      {help && pos && createPortal(
        <div role="note" style={{ position: 'fixed', top: pos.top, left: pos.left, width: 288, zIndex: 60 }}
          className="pointer-events-none rounded-xl border border-[var(--color-border)] bg-[var(--color-surface)] p-3.5 text-left shadow-[0_10px_30px_rgba(0,0,0,0.14)]">
          <p className="text-[13px] font-bold text-[var(--color-text)]">{help.title}</p>
          <p className="mt-1 text-[12.5px] font-normal leading-relaxed text-[var(--color-text-muted)]">{help.text}</p>
        </div>,
        document.body,
      )}
    </div>
  )
}

/** The red number beside a tool link. Nothing is drawn for zero or for a count that has not been read. */
function Badge({ count, label }: { count: number | null; label: string }) {
  if (!count) return null
  return <span className="ml-auto rounded-full bg-red-600 px-1.5 text-[11px] font-bold text-white" aria-label={`${count} ${label}`} title="How many items are waiting for a person. It is the same number as on the page, from the same read.">{count}</span>
}

/**
 * The 5.0.0 shell (design: every `*-home` page; build-plan unit A3): header, the one left menu, and the page.
 *
 * The menu is built from two lists and nothing else: the navigation array (Home, the tools, the footer links) and the coverage
 * catalogue (the PaaS Components section). Nothing here names a component or a tool by hand.
 *
 * The header shows the caller's role, the Agent's state and the Emergency stop button (B3), in the cloud's own colour; a band sits
 * above every page while the stop is on. The badges are the Home tiles' own sums.
 *
 * `?dialog=` and `?panel=` open over any page through the overlay host (F1); an overlay that is not built opens a frame that says so.
 */
export function NextLayout() {
  const params = useParams()
  useNextShortcuts() // ⌘K, ?, / and R (content/shortcuts.ts)
  // `configuration` and `coverage` are not clouds: on those pages the menu shows every cloud's components.
  const scope: CloudScope = parseScope(params.cloud)
  // `/nonsense/attention` names no cloud. It is not "all clouds" under another name: it is an address that does not exist.
  const unknownCloud = params.cloud !== undefined && !(cloudScopes as readonly string[]).includes(params.cloud)
  const cloud = cloudOf(scope)
  const { data } = useCoverage()
  const rows = data?.rows ?? []
  const enabled = useEnabledComponents().data ?? []
  const hidden = useHiddenComponents().data ?? []
  // Until a connection exists, the menu is only what a first-time person can use: Home, Connections and the reference pages. The components and
  // the tools appear with the first connection (any record, even a failing one, so a broken key never hides the tools that fix it). A read that
  // has not finished, or has failed, keeps the full menu: nothing is hidden on a guess.
  const connections$ = useNamespaces()
  const nothingConnected = connections$.isSuccess && connections$.data.length === 0 && !isDemo()
  // The badges are the same sums the Home tiles show, from the same read, so they cannot disagree with the page.
  const components = useComponents(cloud)
  const badges = components.data
    ? {
        needsYou: components.data.components.reduce((sum, c) => sum + c.needsYou, 0),
        approvals: components.data.components.reduce((sum, c) => sum + c.approvals, 0),
      }
    : null
  const home = scope === 'all' ? NEXT_HOME : nextHref(nextEntry('home'), { cloud: scope })
  const tools = nextEntriesIn('tool') as readonly NextPage[]
  // Connections is the first thing under Home: it is where every cloud is connected, so it must not sit at the bottom of a long menu.
  const connections = nextEntry('connections') as NextPage
  const reference = (nextEntriesIn('reference') as readonly NextPage[]).filter((e) => e.id !== 'connections')

  return (
    <div className="min-h-screen bg-[var(--color-surface-muted)]">
      <header className={`flex min-h-[var(--header-height)] flex-wrap items-center gap-x-4 gap-y-2 px-5 py-2 text-white ${cloudHeaderClass[scope]}`}>
        <span className="text-lg font-bold">ServiceHub <span className="font-semibold text-white/80">{import.meta.env.VITE_APP_VERSION}</span></span>
        <span className="text-sm font-semibold">{cloudLabel[scope]}</span>
        <HeaderStatus />
      </header>
      <DemoBanner />
      <StopBand />
      <PausedBand agentsHref={nextHref(nextEntry('agents') as NextPage, { cloud: scope })} />

      <div className="flex">
        <nav aria-label="ServiceHub" className="w-56 shrink-0 self-stretch border-r border-[var(--color-border)] bg-[var(--color-surface)] px-3 pb-6 pt-3">
          <ul aria-label="Cloud" className="mb-2 flex flex-wrap gap-1">
            {cloudScopes.map((s) => (
              <li key={s}>
                <Link
                  to={s === 'all' ? NEXT_HOME : nextHref(nextEntry('home'), { cloud: s })}
                  aria-current={s === scope ? 'true' : undefined}
                  className={`block rounded-md border px-2 py-1 text-xs font-semibold ${s === scope ? 'border-[var(--color-primary-600)] bg-[var(--color-primary-50)] text-[var(--color-primary-700)]' : 'border-[var(--color-border)] text-[var(--color-text)]'}`}
                >
                  {cloudLabel[s]}
                </Link>
              </li>
            ))}
            <li className="flex items-center"><Tip label="menu:Cloud" /></li>
          </ul>

          <MenuLink to={home} end help={{ title: scope === 'all' ? 'All clouds' : 'Home', text: nextEntry(scope === 'all' ? 'home-all' : 'home').description }}>Home</MenuLink>
          <MenuLink to={nextHref(connections)} help={{ title: connections.label, text: connections.description }}>
            <connections.icon className="h-4 w-4 shrink-0" />
            {connections.label}
          </MenuLink>

          {!nothingConnected && <>
          <h2 className={groupTitle}>PaaS Components<Tip label="menu:PaaS Components" /></h2>
          <ul>
            {sidebarComponents(rows, cloud, enabled, hidden).map((row) => (
              <li key={row.componentKey}>
                <MenuLink to={nextHref(nextEntry('component-list'), { cloud: row.cloud, component: row.componentKey })} help={{ title: row.name, text: `Failed messages end up in: ${row.stuckPlace}. ${row.statusWords}.${row.note ? ` ${row.note}.` : ''} Open it to see your connections to ${row.name}.` }}>
                  {row.name}
                  {row.status === 'beingValidated' && <span role="img" aria-label="Not yet validated" title="Not yet validated" className="ml-auto h-2 w-2 shrink-0 rounded-full bg-amber-500" />}
                </MenuLink>
              </li>
            ))}
          </ul>

          <h2 className={groupTitle}>Operational Tools<Tip label="menu:Operational Tools" /></h2>
          <ul>
            {tools.map((entry) => (
              <li key={entry.id}>
                <MenuLink to={nextHref(entry, { cloud: scope })} help={{ title: entry.label, text: entry.description }}>
                  <entry.icon className="h-4 w-4 shrink-0" />
                  {entry.label}
                  {entry.badge === 'needsYou' && <Badge count={badges?.needsYou ?? null} label="need your attention" />}
                  {entry.badge === 'approvals' && <Badge count={badges?.approvals ?? null} label="pending approvals" />}
                </MenuLink>
              </li>
            ))}
          </ul>

          </>}

          <ul className="mt-4 border-t border-[var(--color-border)] pt-3">
            {reference.map((entry) => (
              <li key={entry.id}>
                <MenuLink to={nextHref(entry)} help={{ title: entry.label, text: entry.description }}>
                  <entry.icon className="h-4 w-4 shrink-0" />
                  {entry.label}
                </MenuLink>
              </li>
            ))}
          </ul>
        </nav>

        <main className="min-w-0 flex-1">
          {unknownCloud ? <NotFoundPage /> : <Outlet />}
        </main>
      </div>
      <OverlayHost />
    </div>
  )
}
