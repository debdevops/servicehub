import { lazy, Suspense } from 'react'
import type { RouteObject } from 'react-router-dom'
import { RouteError } from '../components/RouteError'
import { NotFoundPage } from '../pages/NotFoundPage'
import { NEXT_ROOT, nextPages, type NextPage } from './nav/navigation'
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

/**
 * The 5.0.0 route table, derived from the 5.0.0 navigation array and mounted under `/next` beside the 4.2.0 routes.
 * Nothing is listed twice; `tests/web/unit/next/navigation.test.ts` asserts the two stay in step.
 */
export const nextRoute: RouteObject = {
  path: NEXT_ROOT,
  element: <Suspense fallback={loading}><NextLayout /></Suspense>,
  errorElement: <RouteError />,
  children: [
    ...nextPages.map((entry) => ({
      ...(entry.path === '' ? { index: true as const } : { path: entry.path }),
      element: pageFor(entry),
    })),
    { path: '*', element: <NotFoundPage /> },
  ],
}
