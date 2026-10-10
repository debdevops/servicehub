import { DemoEntry } from './components/banners/DemoEntry'
import { storeProvider } from './components/provider/providerScope'
import { enterDemo } from './lib/demo/state'
import type { CloudProvider } from './lib/api/namespaces'
import { lazy, Suspense } from 'react'
import { createBrowserRouter, Navigate, type RouteObject } from 'react-router-dom'
import { AppLayout } from './layouts/AppLayout'
import { HomePage } from './pages/HomePage'
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
      // Fleet Overview merged into Home (D48, 2026-09-27): an old bookmark or link to `/fleet` lands on
      // Home rather than 404ing — every cloud it wanted is right there in "All clouds".
      { path: 'fleet', element: <Navigate to="/" replace /> },
      // An address that names no page. It is not a destination, so it is not in the navigation array.
      { path: '*', element: <NotFoundPage /> },
    ],
  },
  // Published demo URLs (unit 6.5). Outside the layout: they only switch the session into demo mode and open Home.
  // These match only when the router has NO demo basename — i.e. an address that names no cloud (`/demo/other`). When the address
  // is `/demo`, `/demo/aws`… the prefix is the basename (below), react-router strips it, and the page routes above match instead
  // (tests/web/unit/router.basename.test.tsx).
  { path: '/demo', element: <DemoEntry /> },
  { path: '/demo/:provider', element: <DemoEntry /> },
  { path: '/demo/:provider/*', element: <DemoEntry /> },
]

// Served at the origin root (ADR-0014 D3). Only the public demo page is built for another path, and then the router follows it.
const rootBase = import.meta.env.BASE_URL.replace(/\/$/, '')

/**
 * The demo is a section of the address space: `/demo` is all three clouds, `/demo/azure`, `/demo/aws`, `/demo/gcp` one cloud,
 * and every page stays beneath it (`/demo/azure/dlq`, …) so the address bar says where you are and can be shared. The prefix is
 * the router's basename, so no screen needs to know it. Entering also switches this browser session into demo mode.
 */
function demoBase(): string {
  if (typeof window === 'undefined') return ''
  const path = window.location.pathname.slice(rootBase.length)
  const match = /^\/demo(?:\/(azure|aws|gcp))?(?=\/|$)/i.exec(path)
  if (!match) return ''
  enterDemo()
  const cloud = match[1]?.toLowerCase() as CloudProvider | undefined
  if (cloud) storeProvider(cloud)
  return match[0]
}

const basename = `${rootBase}${demoBase()}`
export const router = createBrowserRouter(routes, basename ? { basename } : undefined)
