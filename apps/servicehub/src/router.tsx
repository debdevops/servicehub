import { lazy, Suspense } from 'react'
import { createBrowserRouter, type RouteObject } from 'react-router-dom'
import { AppLayout } from './layouts/AppLayout'
import { HomePage } from './pages/HomePage'
import { PlaceholderPage } from './pages/PlaceholderPage'
import { pages } from './nav/navigation'

// Each built page is its own chunk, so the first paint carries only the shell and Home.
const RecoveryLedgerPage = lazy(() => import('./pages/advanced/RecoveryLedgerPage'))

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
  if (entry.id === 'ledger') {
    return (
      <Suspense fallback={<p role="status" className="px-6 py-6 text-sm">Loading…</p>}>
        <RecoveryLedgerPage />
      </Suspense>
    )
  }
  return <PlaceholderPage entry={entry} />
}

export const routes: RouteObject[] = [
  {
    path: '/',
    element: <AppLayout />,
    children: pages.map((entry) => ({
      // React Router wants the index route rather than a path of '/'.
      ...(entry.path === '/' ? { index: true as const } : { path: entry.path.replace(/^\//, '') }),
      element: pageFor(entry),
    })),
  },
]

export const router = createBrowserRouter(routes)
