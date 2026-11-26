import { lazy, Suspense } from 'react'
import { Navigate, useLocation, useParams, type RouteObject } from 'react-router-dom'
import { RouteError } from '../components/RouteError'
import { NotFoundPage } from '../pages/NotFoundPage'
import { NEXT_HOME, nextEntry, nextHref, nextPages, type NextPage } from './nav/navigation'
import { NextPlaceholderPage } from './pages/NextPlaceholderPage'

// Each built page is its own chunk, exactly as in the 4.2.0 router.
// The shell (and the 52 kB of explanations it carries) loads with the first /next screen, not with the 4.2.0 core loop: the initial chunk has a budget.
const NextLayout = lazy(() => import('./layouts/NextLayout').then((m) => ({ default: m.NextLayout })))
const CoveragePage = lazy(() => import('./pages/CoveragePage'))
const HomePage = lazy(() => import('./pages/HomePage'))
const AllCloudsPage = lazy(() => import('./pages/AllCloudsPage'))
const ComponentListPage = lazy(() => import('./pages/ComponentListPage'))
const ComponentDetailPage = lazy(() => import('./pages/ComponentDetailPage'))
const AttentionPage = lazy(() => import('./pages/AttentionPage'))
const ApprovalsPage = lazy(() => import('./pages/ApprovalsPage'))
const ActivePage = lazy(() => import('./pages/ActivePage'))
const ReplaysPage = lazy(() => import('./pages/ReplaysPage'))
const RulesPage = lazy(() => import('./pages/RulesPage'))
const SignaturesPage = lazy(() => import('./pages/SignaturesPage'))
const LedgerPage = lazy(() => import('./pages/LedgerPage'))
const AgentsPage = lazy(() => import('./pages/AgentsPage'))
const ConfigurationPage = lazy(() => import('./pages/ConfigurationPage'))
const ConnectionsPage = lazy(() => import('./pages/ConnectionsPage'))

const loading = <p role="status" className="px-6 py-6 text-sm">Loading…</p>

/**
 * The page for an entry. As each unit builds a screen, ONE line is added here (a lazy import above and a case below) and the
 * entry's `built` flag is set — nothing else about routing changes. That is the whole procedure for "add a screen".
 */
function pageFor(entry: NextPage) {
  switch (entry.id) {
    case 'coverage':
      return <Suspense fallback={loading}><CoveragePage /></Suspense>
    case 'home-all':
      return <Suspense fallback={loading}><AllCloudsPage /></Suspense>
    case 'home':
      return <Suspense fallback={loading}><HomePage /></Suspense>
    case 'component-list':
      return <Suspense fallback={loading}><ComponentListPage /></Suspense>
    case 'component-detail':
      return <Suspense fallback={loading}><ComponentDetailPage /></Suspense>
    case 'attention':
      return <Suspense fallback={loading}><AttentionPage /></Suspense>
    case 'approvals':
      return <Suspense fallback={loading}><ApprovalsPage /></Suspense>
    case 'active':
      return <Suspense fallback={loading}><ActivePage /></Suspense>
    case 'replays':
      return <Suspense fallback={loading}><ReplaysPage /></Suspense>
    case 'rules':
      return <Suspense fallback={loading}><RulesPage /></Suspense>
    case 'signatures':
      return <Suspense fallback={loading}><SignaturesPage /></Suspense>
    case 'ledger':
      return <Suspense fallback={loading}><LedgerPage /></Suspense>
    case 'agents':
      return <Suspense fallback={loading}><AgentsPage /></Suspense>
    case 'connections':
      return <Suspense fallback={loading}><ConnectionsPage /></Suspense>
    case 'configuration':
      return <Suspense fallback={loading}><ConfigurationPage /></Suspense>
    default:
      return <NextPlaceholderPage entry={entry} />
  }
}

const clouds = ['azure', 'aws', 'gcp']
const tool = (id: string, cloud = 'all') => nextHref(nextEntry(id), { cloud })

/**
 * Where a 4.2.0 address on the first screen went. 4.2.0 kept its tabs and panels in the query of `/` (`/?tab=dlq&provider=aws`,
 * `/?panel=rules`); each is a page now. Null when the query names none of them: then this is simply the first screen.
 */
export function legacyTarget(search: string): string | null {
  const query = new URLSearchParams(search)
  const provider = query.get('provider')?.toLowerCase() ?? ''
  const cloud = clouds.includes(provider) ? provider : 'all'
  const tab = query.get('tab')
  if (tab === 'dlq') return cloud === 'all' ? NEXT_HOME : nextHref(nextEntry('home'), { cloud })
  if (tab === 'active') return tool('active', cloud)
  if (tab === 'replayed') return tool('replays', cloud)
  const panel = query.get('panel')
  if (panel === 'rules') return tool('rules', cloud)
  if (panel === 'approvals') return tool('approvals', cloud)
  return null
}

/** The first screen, or the page a 4.2.0 bookmark of it now means. A bookmark must never 404 (I1). */
function FirstScreen() {
  const { search } = useLocation()
  const target = legacyTarget(search)
  return target ? <Navigate to={target} replace /> : pageFor(nextEntry('home-all') as NextPage)
}

/** `/next/…` was this layout's address until the cutover; the same page is now one level up. */
function FromNext() {
  const rest = useParams()['*'] ?? ''
  const { search } = useLocation()
  return <Navigate to={`/${rest}${search}`} replace />
}

/**
 * The route table, derived from the navigation array and mounted at the root. Nothing is listed twice;
 * `tests/web/unit/next/navigation.test.ts` asserts the two stay in step.
 *
 * The fixed paths below it are 4.2.0 addresses, kept as redirects (ids start `legacy:`). A fixed path outranks `:cloud`, so none
 * of them can be taken for a cloud.
 */
export const nextRoute: RouteObject = {
  path: NEXT_HOME,
  element: <Suspense fallback={loading}><NextLayout /></Suspense>,
  errorElement: <RouteError />,
  children: [
    ...nextPages.map((entry) => ({
      ...(entry.path === '' ? { index: true as const, element: <FirstScreen /> } : { path: entry.path, element: pageFor(entry) }),
    })),
    { id: 'legacy:advanced', path: 'advanced', element: <Navigate to={NEXT_HOME} replace /> },
    { id: 'legacy:advanced/ledger', path: 'advanced/ledger', element: <Navigate to={tool('ledger')} replace /> },
    { id: 'legacy:advanced/signatures', path: 'advanced/signatures', element: <Navigate to={tool('signatures')} replace /> },
    { id: 'legacy:advanced/agents', path: 'advanced/agents', element: <Navigate to={tool('agents')} replace /> },
    { id: 'legacy:fleet', path: 'fleet', element: <Navigate to={NEXT_HOME} replace /> },
    { id: 'legacy:next', path: 'next', element: <Navigate to={NEXT_HOME} replace /> },
    { id: 'legacy:next/*', path: 'next/*', element: <FromNext /> },
    { path: '*', element: <NotFoundPage /> },
  ],
}
