import { createBrowserRouter, type RouteObject } from 'react-router-dom'
import { AppLayout } from './layouts/AppLayout'
import { PlaceholderPage } from './pages/PlaceholderPage'
import { pages } from './nav/navigation'

/**
 * The route table, derived from the navigation array. Only PAGES are routes (D45): tabs, panels and
 * modals are URL states (?tab= · ?panel= · ?modal=) on the page you are on.
 *
 * Nothing is listed twice. A screen cannot exist in the sidebar without a route, or have a route
 * nothing links to — which is the drift that produced real bugs in 4.0.0. `navigation.test.ts`
 * asserts the two stay in step.
 *
 * As each wave builds a screen, its placeholder is replaced by a lazy import of the real page.
 * Nothing else about routing changes.
 */
export const routes: RouteObject[] = [
  {
    path: '/',
    element: <AppLayout />,
    children: pages.map((entry) => ({
      // React Router wants the index route rather than a path of '/'.
      ...(entry.path === '/' ? { index: true as const } : { path: entry.path.replace(/^\//, '') }),
      element: <PlaceholderPage entry={entry} />,
    })),
  },
]

export const router = createBrowserRouter(routes)
