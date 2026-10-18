import { lazy, Suspense } from 'react'
import type { RouteObject } from 'react-router-dom'
import { RouteError } from '../components/RouteError'
import { NotFoundPage } from '../pages/NotFoundPage'
import { NextLayout } from './layouts/NextLayout'
import { NEXT_ROOT, nextPages, type NextPage } from './nav/navigation'
import { NextPlaceholderPage } from './pages/NextPlaceholderPage'

// Each built page is its own chunk, exactly as in the 4.2.0 router.
const CoveragePage = lazy(() => import('./pages/CoveragePage'))
const HomePage = lazy(() => import('./pages/HomePage'))
const AllCloudsPage = lazy(() => import('./pages/AllCloudsPage'))
const ComponentListPage = lazy(() => import('./pages/ComponentListPage'))
const ComponentDetailPage = lazy(() => import('./pages/ComponentDetailPage'))

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
  element: <NextLayout />,
  errorElement: <RouteError />,
  children: [
    ...nextPages.map((entry) => ({
      ...(entry.path === '' ? { index: true as const } : { path: entry.path }),
      element: pageFor(entry),
    })),
    { path: '*', element: <NotFoundPage /> },
  ],
}
