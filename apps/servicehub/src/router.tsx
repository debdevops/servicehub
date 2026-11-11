import { DemoEntry } from './components/banners/DemoEntry'
import { enterDemo } from './lib/demo/state'
import { createBrowserRouter, type RouteObject } from 'react-router-dom'
import { nextRoute } from './next/router'

/**
 * The route table. Every page comes from the one navigation array (`next/nav/navigation.ts`) through `nextRoute`; a screen
 * cannot be in the menu without a route, or have a route nothing links to (`tests/web/unit/next/navigation.test.ts`).
 * Dialogs and panels are URL states (`?dialog=` · `?panel=`) on the page you are on.
 *
 * Until the cutover (build-plan unit I1, 2026-11-11) the 4.2.0 layout was mounted here and this one under `/next`. The 4.2.0
 * addresses are kept as redirects inside `nextRoute`.
 */
export const routes: RouteObject[] = [
  nextRoute,
  // Only reached when the router has no demo basename, which is never in a browser (see `demoBase`): kept so `/demo` still
  // enters the demo when the route table is mounted without one (a test, a memory router).
  { path: '/demo', element: <DemoEntry /> },
  { path: '/demo/:provider', element: <DemoEntry /> },
  { path: '/demo/:provider/*', element: <DemoEntry /> },
]

// Served at the origin root (ADR-0014 D3). Only the public demo page is built for another path, and then the router follows it.
const rootBase = import.meta.env.BASE_URL.replace(/\/$/, '')

/**
 * The demo is a section of the address space: everything under `/demo` is the same app on made-up data, so the address bar
 * says where you are and can be shared. `/demo` is all three clouds; `/demo/azure`, `/demo/aws`, `/demo/gcp` one cloud; every
 * page stays beneath (`/demo/aws/attention`). The prefix is the router's basename, so no screen needs to know it, and the cloud
 * is the ordinary first segment after it. Entering also switches this browser session into demo mode.
 */
function demoBase(): string {
  if (typeof window === 'undefined') return ''
  const path = window.location.pathname.slice(rootBase.length)
  if (!/^\/demo(?=\/|$)/i.test(path)) return ''
  enterDemo()
  return path.slice(0, '/demo'.length)
}

const basename = `${rootBase}${demoBase()}`
export const router = createBrowserRouter(routes, basename ? { basename } : undefined)
