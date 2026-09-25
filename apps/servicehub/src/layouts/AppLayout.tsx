import { useEffect } from 'react'
import { Link, NavLink, Outlet, useLocation } from 'react-router-dom'
import { CloudProviders } from '../components/provider/CloudProviders'
import { ProviderScopeProvider } from '../components/provider/ProviderScopeProvider'
import { SurfaceSwitch } from '../components/ui/SurfaceSwitch'
import { OverlayHost } from '../components/overlays/OverlayHost'
import { useEventStream } from '../hooks/useEventStream'
import { useNamespaces } from '../hooks/useNamespaces'
import { readLastAdvancedPath, rememberAdvancedPath } from '../lib/lastAdvancedPage'
import { connectedProviders } from '../lib/providers'
import { ADVANCED_ROOT, hrefOf, landingPath, surfaceOf, visibleEntries, type NavEntry, type NavGroup, type OverlayEntry } from '../nav/navigation'
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
  const { pathname } = useLocation()
  const surface = surfaceOf(pathname)
  const onSurface = entries.filter((e) => e.surface === surface)
  useEffect(() => rememberAdvancedPath(pathname), [pathname])

  return (
    <ProviderScopeProvider connected={providers.map((p) => p.provider)}>
      <LandingRedirect ready={loaded} connectedCloudCount={cloudCount} />
      <div className="min-h-screen" data-surface={surface}>
        <header
          className="sticky top-0 z-20 flex items-center gap-4 border-b border-[var(--color-border)] bg-[var(--color-surface)] px-5"
          style={{ height: 'var(--header-height)' }}
        >
          <div className="flex items-center gap-2.5">
            <span
              aria-hidden="true"
              className="flex h-[34px] w-[34px] items-center justify-center rounded-[10px] text-[17px] font-extrabold text-white shadow-[0_2px_6px_rgba(2,132,199,0.28)]"
              style={{ background: 'linear-gradient(140deg, #38bdf8, #0369a1 70%)' }}
            >
              S
            </span>
            <div>
              <div className="text-[17px] font-extrabold leading-[1.1] tracking-tight text-[var(--color-text)]">
                Service<span className="text-[var(--color-primary-600)]">Hub</span>
              </div>
              <div className="whitespace-nowrap text-[10.5px] leading-[1.2] text-[var(--color-text-muted)]">
                See messages. Fix issues. Keep systems moving.
              </div>
            </div>
          </div>
          <div className="ml-auto">
            <SurfaceSwitch
              surface={surface}
              simpleHref={landingPath(cloudCount)}
              advancedHref={surface === 'advanced' ? pathname : readLastAdvancedPath(ADVANCED_ROOT)}
            />
          </div>
        </header>

        <div className="flex">
          <nav
            aria-label="Main"
            className="sticky shrink-0 overflow-y-auto border-r border-[var(--color-border)] bg-[var(--color-surface)] px-3 pb-[18px] pt-3.5"
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
              <h2 className="px-2 pb-[7px] pt-[18px] text-[10px] font-bold uppercase tracking-[0.9px] text-[#9ca3af]">
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
                See messages. Fix issues. Keep your systems moving.
              </div>
              <div className="mt-2 font-mono text-[10px] text-[#9ca3af]">
                v{import.meta.env.VITE_APP_VERSION}
              </div>
            </div>
          </nav>

          <main className="min-w-0 flex-1">
            <Outlet />
          </main>
        </div>

        <OverlayHost connectedCloudCount={cloudCount} />
      </div>
    </ProviderScopeProvider>
  )
}

const itemClass = 'mb-0.5 flex items-center gap-[11px] rounded-[9px] px-[13px] py-[8.5px] text-[13.5px] font-medium transition-colors'
const idleClass = 'text-[#374151] hover:bg-[var(--color-primary-50)] hover:text-[var(--color-primary-700)]'
const activeClass = 'font-semibold text-white shadow-[0_2px_6px_rgba(2,132,199,0.3)] [background:linear-gradient(100deg,#0284c7,#0369a1)]'

function NavSection({ label, entries, bare }: { label?: string; entries: readonly NavEntry[]; bare?: boolean }) {
  const { pathname, search } = useLocation()
  if (entries.length === 0) return null

  return (
    <div className={bare ? '' : 'mb-6'}>
      {label && (
        <h2 className="px-2 pb-[7px] pt-[18px] text-[10px] font-bold uppercase tracking-[0.9px] text-[#9ca3af]">
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
              // Tabs, panels and modals are URL states on a page, not routes (D45). Opening a panel or
              // modal keeps the rest of the URL — the tab you are on stays put behind it.
              <Link to={entry.kind === 'tab' ? hrefOf(entry) : overlayHref(entry, pathname, search)} title={entry.description} className={[itemClass, idleClass].join(' ')}>
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
  const params = new URLSearchParams(search)
  params.set(entry.kind, entry.value)
  return `${pathname}?${params.toString()}`
}
