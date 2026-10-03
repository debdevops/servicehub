import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, NavLink, Outlet, useLocation, useNavigate, useNavigationType } from 'react-router-dom'
import { ChevronLeft, ChevronRight, Menu, Search } from 'lucide-react'
import { CommandPalette } from '../components/search/CommandPalette'
import { useShortcuts } from '../hooks/useShortcuts'
import { CloudProviders } from '../components/provider/CloudProviders'
import { ProviderScopeProvider } from '../components/provider/ProviderScopeProvider'
import { SurfaceSwitch } from '../components/ui/SurfaceSwitch'
import { Bell } from '../components/pending/Bell'
import { AccountMenu } from '../components/AccountMenu'
import { SafetyBanners } from '../components/banners/SafetyBanners'
import { DemoBanner } from '../components/banners/DemoEntry'
import { usePendingWork } from '../hooks/usePendingWork'
import { rememberPage } from '../lib/preferences'
import { EscalationToast } from '../components/pending/EscalationToast'
import { ActionNotice } from '../components/ui/ActionNotice'
import { ReplayAllDock } from '../components/message/ReplayAllDock'
import { OverlayHost } from '../components/overlays/OverlayHost'
import { useEventStream } from '../hooks/useEventStream'
import { useNamespaces } from '../hooks/useNamespaces'
import { readLastAdvancedPath, rememberAdvancedPath } from '../lib/lastAdvancedPage'
import { connectedProviders } from '../lib/providers'
import { withoutDrawers } from '../lib/urlState'
import { ADVANCED_ROOT, hrefOf, navigation, landingPath, surfaceOf, visibleEntries, type NavEntry, type NavGroup, type OverlayEntry } from '../nav/navigation'
import { LandingRedirect } from './LandingRedirect'

/**
 * The application frame: a 56px header, one 260px sidebar, content, and the overlay host.
 *
 * One sidebar, deliberately. Several navigation surfaces would drift apart from each other.
 *
 * Every entry here comes from the navigation array (ARCHITECTURE §4.5) and is shown only when what
 * is connected makes it meaningful. The Clouds section is derived from the connected namespaces —
 * there is nothing to configure and nothing greyed out.
 */
export function AppLayout() {
  useEventStream()
  const namespaces = useNamespaces()
  const providers = connectedProviders(namespaces.data ?? [])
  const cloudCount = providers.length
  const entries = visibleEntries(cloudCount)
  const inGroup = (group: NavGroup) => onSurface.filter((e) => e.group === group)
  const loaded = namespaces.isSuccess

  // The URL is the surface (ADR-0016 D2). The layout is the only place that asks — pages never do.
  const { pathname, search, hash } = useLocation()
  const surface = surfaceOf(pathname)
  const onSurface = entries.filter((e) => e.surface === surface)
  useEffect(() => rememberAdvancedPath(pathname), [pathname])
  useEffect(() => rememberPage(pathname, search), [pathname, search])
  // A client-side route change never auto-scrolls to a `#fragment` the way a full page load does (e.g.
  // "Look at the Agent bar" on a Needs-you card, which points at `#agent-bar` further up Home).
  useEffect(() => {
    if (!hash) return
    document.getElementById(hash.slice(1))?.scrollIntoView({ block: 'start' })
  }, [pathname, hash])

  // Below the large breakpoint the sidebar is a slide-in menu; choosing anything closes it.
  const [menuOpen, setMenuOpen] = useState(false)
  const [searchOpen, setSearchOpen] = useState(false)
  const simpleHref = landingPath()
  const advancedHref = surface === 'advanced' ? pathname : readLastAdvancedPath(ADVANCED_ROOT)
  useShortcuts({ openSearch: useCallback(() => setSearchOpen(true), []), simpleHref, advancedHref })
  useEffect(() => setMenuOpen(false), [pathname, search])
  useEffect(() => {
    if (!menuOpen) return
    const esc = (e: KeyboardEvent) => e.key === 'Escape' && setMenuOpen(false)
    document.addEventListener('keydown', esc)
    return () => document.removeEventListener('keydown', esc)
  }, [menuOpen])

  return (
    <ProviderScopeProvider connected={providers.map((p) => p.provider)}>
      <LandingRedirect ready={loaded} />
      <div className="min-h-screen" data-surface={surface}>
        {/* One shared slot, above everything, full width — never split across the header/sidebar
            boundary. Order when more than one is true: emergency › paused › demo (unit 6.10). */}
        {loaded && cloudCount > 0 && <SafetyBanners />}
        <DemoBanner />
        <header
          className="sticky top-0 z-20 flex items-center gap-1.5 border-b border-[var(--color-border)] bg-[var(--color-surface)] px-3 sm:gap-4 sm:px-5"
          style={{ height: 'var(--header-height)' }}
        >
          <button
            type="button"
            aria-label="Menu"
            aria-expanded={menuOpen}
            aria-controls="main-nav"
            onClick={() => setMenuOpen((o) => !o)}
            className="-ml-2 rounded-lg p-2 text-[var(--color-text-muted)] hover:bg-[var(--color-surface-muted)] nav:hidden"
          >
            <Menu className="h-5 w-5" aria-hidden="true" />
          </button>
          <div className="flex items-center gap-2.5">
            <span
              aria-hidden="true"
              className="flex h-[34px] w-[34px] items-center justify-center rounded-[10px] text-[17px] font-extrabold text-white shadow-[0_2px_6px_rgba(2,132,199,0.28)]"
              style={{ background: 'linear-gradient(140deg, #38bdf8, #0369a1 70%)' }}
            >
              S
            </span>
            <div className="max-[420px]:hidden">
              <div className="text-[17px] font-extrabold leading-[1.1] tracking-tight text-[var(--color-text)]">
                Service<span className="text-[var(--color-primary-600)]">Hub</span>
              </div>
              <div className="hidden whitespace-nowrap text-[10.5px] leading-[1.2] text-[var(--color-text-muted)] sm:block">
                Keep messages moving.
              </div>
            </div>
          </div>
          <HistoryButtons />
          <button
            type="button"
            onClick={() => setSearchOpen(true)}
            className="ml-auto hidden min-w-0 w-full max-w-[360px] items-center gap-2 overflow-hidden rounded-[10px] border border-[var(--color-border)] bg-[var(--color-surface-muted)] px-3 py-2 text-left text-[13px] text-[var(--color-text-muted)] hover:border-[var(--color-primary-600)] md:flex"
          >
            <Search className="h-4 w-4 shrink-0" aria-hidden="true" />
            <span className="min-w-0 flex-1 truncate">Search clouds, queues and places…</span>
            <kbd className="shrink-0 rounded border border-[var(--color-border)] bg-[var(--color-surface)] px-1.5 text-[11px]">⌘K</kbd>
          </button>
          <button type="button" aria-label="Search" onClick={() => setSearchOpen(true)} className="ml-auto rounded-lg p-2 text-[var(--color-text-muted)] hover:bg-[var(--color-surface-muted)] md:hidden">
            <Search className="h-5 w-5" aria-hidden="true" />
          </button>
          <div className="flex items-center gap-1.5 sm:gap-2 md:ml-0">
            {loaded && cloudCount > 0 && <Bell />}
            <SurfaceSwitch
              surface={surface}
              simpleHref={simpleHref}
              advancedHref={advancedHref}
            />
            <AccountMenu />
          </div>
        </header>

        <div className="flex">
          {menuOpen && <div className="fixed inset-0 z-20 bg-black/30 nav:hidden" aria-hidden="true" onClick={() => setMenuOpen(false)} />}
          <nav
            id="main-nav"
            aria-label="Main"
            className={`sticky shrink-0 overflow-y-auto max-nav:fixed max-nav:left-0 max-nav:z-30 max-nav:transition-transform ${menuOpen ? '' : 'max-nav:invisible max-nav:-translate-x-full'} border-r border-[var(--color-border)] bg-[var(--color-surface)] px-3 pb-[18px] pt-3.5`}
            style={{
              width: 'var(--sidebar-width)',
              top: 'var(--header-height)',
              height: 'calc(100vh - var(--header-height))',
            }}
          >
            <NavSection entries={inGroup('primary')} />
            {loaded && <NavSection label="Work" entries={inGroup('work')} />}
            <NavSection label={surface === 'advanced' ? 'Advanced' : undefined} entries={inGroup('advanced')} />

            {surface === 'simple' && (
            <div className="mb-6">
              <h2 className="px-2 pb-[7px] pt-[18px] text-[10px] font-bold uppercase tracking-[0.9px] text-[var(--color-text-muted)]">
                Clouds
              </h2>
              {namespaces.isPending && <p className="px-3 py-2 text-sm text-[var(--color-text-muted)]">Loading your clouds…</p>}
              {namespaces.isError && (
                <div className="px-3 py-2 text-sm text-[var(--color-text-muted)]">
                  <p>Couldn’t load your clouds.</p>
                  <button
                    type="button"
                    onClick={() => void namespaces.refetch()}
                    className="mt-1 text-[var(--color-primary-700)] hover:underline"
                  >
                    Try again
                  </button>
                </div>
              )}
              {loaded && <CloudProviders providers={providers} />}
              <NavSection entries={inGroup('clouds')} bare />
            </div>
            )}

            <NavSection entries={inGroup('utility')} />

            <div className="mt-5 rounded-[11px] border border-[var(--color-border)] bg-[var(--color-surface-muted)] p-3.5">
              <div className="text-[12.5px] font-bold text-[var(--color-primary-700)]">ServiceHub</div>
              <div className="mt-0.5 text-[11px] leading-[1.4] text-[var(--color-text-muted)]">
                Keep messages moving.
              </div>
              <div className="mt-2 font-mono text-[10px] text-[var(--color-text-muted)]">
                v{import.meta.env.VITE_APP_VERSION}
              </div>
            </div>
          </nav>

          <main className="min-w-0 flex-1">
            <Outlet />
          </main>
        </div>

        <OverlayHost connectedCloudCount={cloudCount} />
        <CommandPalette open={searchOpen} onClose={() => setSearchOpen(false)} connectedCloudCount={cloudCount} />
      </div>
      {loaded && cloudCount > 0 && <EscalationToast />}
      <ActionNotice />
      <ReplayAllDock />
    </ProviderScopeProvider>
  )
}

const itemClass = 'mb-0.5 flex items-center gap-[11px] rounded-[9px] px-[13px] py-[8.5px] text-[13.5px] font-medium transition-colors'
const idleClass = 'text-[#374151] hover:bg-[var(--color-primary-50)] hover:text-[var(--color-primary-700)]'
const activeClass = 'font-semibold text-white shadow-[0_2px_6px_rgba(3,105,161,0.3)] [background:linear-gradient(100deg,#0369a1,#075985)]'

/** The Ledger's sidebar badge: how many things wait for a person — the bell's count, from the same query (5.9). */
function WaitingBadge() {
  const { data } = usePendingWork()
  if (!data?.total) return null
  return <span className="ml-auto whitespace-nowrap rounded-full bg-[#fef3c7] px-2 py-0.5 text-[11px] font-semibold text-[#92400e]" title={`${data.total} waiting for a person`}>{data.total} waiting</span>
}

function NavSection({ label, entries, bare }: { label?: string; entries: readonly NavEntry[]; bare?: boolean }) {
  const { pathname, search } = useLocation()
  if (entries.length === 0) return null

  // A tab or panel open on Home is the place you are: it is lit instead of Home (exactly one row is highlighted).
  const params = new URLSearchParams(search)
  // Auto Replay opens over whatever tab was behind it; that tab is then not "where you are", so exactly one of the two is lit.
  const rulesOpen = params.get('panel') === 'rules'
  const isHere = (e: NavEntry) => pathname === '/' && ((e.kind === 'tab' && !rulesOpen && params.get('tab') === e.value) || (e.kind === 'panel' && e.id === 'auto-replay' && params.get('panel') === e.value))
  const here = navigation.some(isHere)

  return (
    <div className={bare ? '' : 'mb-6'}>
      {label && (
        <h2 className="px-2 pb-[7px] pt-[18px] text-[10px] font-bold uppercase tracking-[0.9px] text-[var(--color-text-muted)]">
          {label}
        </h2>
      )}
      <ul>
        {entries.map((entry) => (
          <li key={entry.id}>
            {entry.kind === 'page' && entry.path === '/' && here ? (
              // Home while a Home tab or panel is the place you are: not current, to the eye or to a screen reader.
              <Link to={entry.path} title={entry.description} className={[itemClass, idleClass].join(' ')}>
                <entry.icon className="h-4 w-4 shrink-0" />
                {entry.label}
              </Link>
            ) : entry.kind === 'page' ? (
              <NavLink
                to={entry.path}
                // `end` for the roots: "/" and the Advanced Overview would otherwise match every page beneath them,
                // leaving two rows highlighted. The design highlights exactly one.
                end={entry.path === '/' || entry.path === ADVANCED_ROOT}
                title={entry.description}
                className={({ isActive }) => [itemClass, isActive ? activeClass : idleClass].join(' ')}
              >
                <entry.icon className="h-4 w-4 shrink-0" />
                {entry.label}
                {entry.id === 'ledger' && <WaitingBadge />}
              </NavLink>
            ) : (
              // Tabs, panels and modals are URL states on a page, not routes (D45). Opening a panel or
              // modal keeps the rest of the URL — the tab you are on stays put behind it.
              <Link to={entry.kind === 'tab' ? hrefOf(entry) : overlayHref(entry, pathname, search)} title={entry.description} aria-current={isHere(entry) ? 'page' : undefined} className={[itemClass, isHere(entry) ? activeClass : idleClass].join(' ')}>
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

function overlayHref(entry: OverlayEntry, pathname: string, search: string): string {
  // A drawer left open beside the page is put away: the click opens the one window it names.
  const params = withoutDrawers(new URLSearchParams(search))
  params.set(entry.kind, entry.value)
  return `${pathname}?${params.toString()}`
}

/**
 * Back and forward, in the app (as in 4.0.0), so a person is never sent to the browser's own arrows. They walk the same
 * history the browser keeps. Back is off on the first page of the visit; forward is off until you have gone back.
 * React Router numbers each entry (`history.state.idx`), so both are known without guessing.
 */
function HistoryButtons() {
  const navigate = useNavigate()
  const navType = useNavigationType()
  useLocation() // re-render on every navigation so the arrows track it
  const furthest = useRef(0)
  const idx = (window.history.state as { idx?: number } | null)?.idx ?? 0
  // A new page (push) wipes what was "ahead"; going back or a same-page filter change (replace) leaves it in place.
  if (navType === 'PUSH' || idx > furthest.current) furthest.current = idx
  const canBack = idx > 0
  const canForward = idx < furthest.current
  const cls = 'rounded-lg p-2 text-[var(--color-text-muted)] hover:bg-[var(--color-surface-muted)] disabled:opacity-40'
  return (
    <div className="hidden items-center md:flex" role="group" aria-label="History">
      <button type="button" aria-label="Back" title="Back" disabled={!canBack} onClick={() => navigate(-1)} className={cls}>
        <ChevronLeft className="h-5 w-5" aria-hidden="true" />
      </button>
      <button type="button" aria-label="Forward" title="Forward" disabled={!canForward} onClick={() => navigate(1)} className={cls}>
        <ChevronRight className="h-5 w-5" aria-hidden="true" />
      </button>
    </div>
  )
}
