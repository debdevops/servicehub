import { DemoEntry } from './components/banners/DemoEntry'
import { lazy, Suspense } from 'react'
import { createBrowserRouter, type RouteObject } from 'react-router-dom'
import { AppLayout } from './layouts/AppLayout'
import { HomePage } from './pages/HomePage'
import { FleetPage } from './pages/FleetPage'
import { PlaceholderPage } from './pages/PlaceholderPage'
import { NotFoundPage } from './pages/NotFoundPage'
import { RouteError } from './components/RouteError'
import { pages } from './nav/navigation'

// Each built page is its own chunk, so the first paint carries only the shell and Home.
const RecoveryLedgerPage = lazy(() => import('./pages/advanced/RecoveryLedgerPage'))
const FailureSignaturesPage = lazy(() => import('./pages/advanced/FailureSignaturesPage'))
const AgentsPage = lazy(() => import('./pages/advanced/AgentsPage'))
const AdvancedOverviewPage = lazy(() => import('./pages/advanced/AdvancedOverviewPage'))

/**
 * The route table, derived from the navigation array. Only PAGES are routes (D45): tabs, panels and
 * modals are URL states (?tab= · ?panel= · ?modal=) on the page you are on.
 *
 * Nothing is listed twice. A screen cannot exist in the sidebar without a route, or have a route
 * nothing links to — that drift is how navigation bugs happen. `navigation.test.ts`
 * asserts the two stay in step.
 *
 * As each wave builds a screen, its placeholder is replaced by a lazy import of the real page.
 * Nothing else about routing changes.
 */
function pageFor(entry: (typeof pages)[number]) {
  if (entry.id === 'home') return <HomePage />
  if (entry.id === 'fleet') return <FleetPage />
  if (entry.id === 'ledger') {
    return (
      <Suspense fallback={<p role="status" className="px-6 py-6 text-sm">Loading…</p>}>
        <RecoveryLedgerPage />
      </Suspense>
    )
  }
  if (entry.id === 'signatures') {
    return (
      <Suspense fallback={<p role="status" className="px-6 py-6 text-sm">Loading…</p>}>
        <FailureSignaturesPage />
      </Suspense>
    )
  }
  if (entry.id === 'advanced-overview') {
    return (
      <Suspense fallback={<p role="status" className="px-6 py-6 text-sm">Loading…</p>}>
        <AdvancedOverviewPage />
      </Suspense>
    )
  }
  if (entry.id === 'agents') {
    return (
      <Suspense fallback={<p role="status" className="px-6 py-6 text-sm">Loading…</p>}>
        <AgentsPage />
      </Suspense>
    )
  }
  return <PlaceholderPage entry={entry} />
}

export const routes: RouteObject[] = [
  {
    path: '/',
    element: <AppLayout />,
    errorElement: <RouteError />,
    children: [
      ...pages.map((entry) => ({
        // React Router wants the index route rather than a path of '/'.
        ...(entry.path === '/' ? { index: true as const } : { path: entry.path.replace(/^\//, '') }),
        element: pageFor(entry),
      })),
      // An address that names no page. It is not a destination, so it is not in the navigation array.
      { path: '*', element: <NotFoundPage /> },
    ],
  },
  // Published demo URLs (unit 6.5). Outside the layout: they only switch the session into demo mode and open Home.
  { path: '/demo/:provider', element: <DemoEntry /> },
  { path: '/demo/:provider/*', element: <DemoEntry /> },
]

export const router = createBrowserRouter(routes)
