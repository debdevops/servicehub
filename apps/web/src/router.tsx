import { createBrowserRouter, Navigate } from 'react-router-dom';
import { lazy, Suspense } from 'react';
import { MainLayout } from '@/components/layout';
// Imported directly from its own module, not the @/pages barrel — importing anything from
// the barrel forces Rollup to evaluate every page it re-exports (including the ones lazily
// imported below), defeating the lazy-loading entirely (Rollup's own
// INEFFECTIVE_DYNAMIC_IMPORT warning surfaces this if the barrel is used here).
import { WelcomePage } from './pages/WelcomePage';
import { RouteErrorPage } from './pages/RouteErrorPage';
import { NotFoundPage } from './pages/NotFoundPage';
import { DemoModeProvider } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { DEMO_NAMESPACE_IDS } from '@servicehub/ui-shared/lib/demo/mockProviders';

// Lazy-load heavy pages to improve initial bundle size and cold-start performance
const DashboardPageLazy = lazy(() => import('./pages/DashboardPage'));
const FleetPageLazy = lazy(() => import('./pages/FleetPage'));
const DlqHistoryPageLazy = lazy(() => import('./pages/DlqHistoryPage'));
const CloudBridgePageLazy = lazy(() => import('./pages/CloudBridgePage').then(m => ({ default: m.CloudBridgePage })));
const CrossCloudTracePageLazy = lazy(() => import('./pages/CrossCloudTracePage').then(m => ({ default: m.CrossCloudTracePage })));
const AuditPageLazy = lazy(() => import('./pages/AuditPage').then(m => ({ default: m.AuditPage })));
const MessagesOverviewPageLazy = lazy(() => import('./pages/MessagesOverviewPage'));
const MessagesPageLazy = lazy(() => import('./pages/MessagesPage').then(m => ({ default: m.MessagesPage })));
const ConnectPageLazy = lazy(() => import('./pages/ConnectPage').then(m => ({ default: m.ConnectPage })));
const RulesPageLazy = lazy(() => import('./pages/RulesPage').then(m => ({ default: m.RulesPage })));
const HealthPageLazy = lazy(() => import('./pages/HealthPage').then(m => ({ default: m.HealthPage })));
const HelpPageLazy = lazy(() => import('./pages/HelpPage').then(m => ({ default: m.HelpPage })));
const ScheduledMessagesPageLazy = lazy(() => import('./pages/ScheduledMessagesPage').then(m => ({ default: m.ScheduledMessagesPage })));
const SecurityPageLazy = lazy(() => import('./pages/SecurityPage').then(m => ({ default: m.SecurityPage })));

// Loading fallback component (co-located here intentionally — used only by router)
// eslint-disable-next-line react-refresh/only-export-components
function PageLoading() {
  return (
    <div className="flex items-center justify-center h-full bg-gray-50">
      <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-600" />
    </div>
  );
}

/**
 * Demo Layouts — MainLayout wrapped with DemoModeProvider for each cloud.
 *
 * Architecture decision: The demo routes share the SAME path namespace as the
 * real app (e.g. /demo/azure/messages, /demo/aws/dashboard) but are wrapped in
 * DemoModeProvider so all hooks return mock data. The Sidebar and Quick Access
 * navigation use relative paths that stay within the demo sub-tree.
 *
 * Each demo provider wraps MainLayout, which renders:
 *   - Real Header (shows mock namespace in connection status)
 *   - DemoModeBanner (amber banner with cloud-provider branding)
 *   - Real Sidebar (shows mock queues/topics from demo hooks)
 *   - Real pages via <Outlet />
 */
// eslint-disable-next-line react-refresh/only-export-components
function DemoAzureLayout() {
  return (
    <DemoModeProvider cloudProvider="azure">
      <MainLayout />
    </DemoModeProvider>
  );
}

// eslint-disable-next-line react-refresh/only-export-components
function DemoAwsLayout() {
  return (
    <DemoModeProvider cloudProvider="aws">
      <MainLayout />
    </DemoModeProvider>
  );
}

// eslint-disable-next-line react-refresh/only-export-components
function DemoGcpLayout() {
  return (
    <DemoModeProvider cloudProvider="gcp">
      <MainLayout />
    </DemoModeProvider>
  );
}

// Shared page children — EXACT same pages as the real app
const sharedChildren = [
  {
    path: 'messages',
    element: (
      <Suspense fallback={<PageLoading />}>
        <MessagesPageLazy />
      </Suspense>
    ),
  },
  {
    path: 'messages-overview',
    element: (
      <Suspense fallback={<PageLoading />}>
        <MessagesOverviewPageLazy />
      </Suspense>
    ),
  },
  {
    path: 'connect',
    element: (
      <Suspense fallback={<PageLoading />}>
        <ConnectPageLazy />
      </Suspense>
    ),
  },
  {
    path: 'rules',
    element: (
      <Suspense fallback={<PageLoading />}>
        <RulesPageLazy />
      </Suspense>
    ),
  },
  {
    path: 'health',
    element: (
      <Suspense fallback={<PageLoading />}>
        <HealthPageLazy />
      </Suspense>
    ),
  },
  {
    path: 'help',
    element: (
      <Suspense fallback={<PageLoading />}>
        <HelpPageLazy />
      </Suspense>
    ),
  },
  {
    path: 'scheduled',
    element: (
      <Suspense fallback={<PageLoading />}>
        <ScheduledMessagesPageLazy />
      </Suspense>
    ),
  },
  {
    path: 'security',
    element: (
      <Suspense fallback={<PageLoading />}>
        <SecurityPageLazy />
      </Suspense>
    ),
  },
  {
    path: 'dashboard',
    element: (
      <Suspense fallback={<PageLoading />}>
        <DashboardPageLazy />
      </Suspense>
    ),
  },
  {
    path: 'fleet',
    element: (
      <Suspense fallback={<PageLoading />}>
        <FleetPageLazy />
      </Suspense>
    ),
  },
  {
    path: 'dlq-history',
    element: (
      <Suspense fallback={<PageLoading />}>
        <DlqHistoryPageLazy />
      </Suspense>
    ),
  },
  {
    path: 'cloud-bridge',
    element: (
      <Suspense fallback={<PageLoading />}>
        <CloudBridgePageLazy />
      </Suspense>
    ),
  },
  {
    path: 'cross-cloud-trace',
    element: (
      <Suspense fallback={<PageLoading />}>
        <CrossCloudTracePageLazy />
      </Suspense>
    ),
  },
  {
    path: 'audit',
    element: (
      <Suspense fallback={<PageLoading />}>
        <AuditPageLazy />
      </Suspense>
    ),
  },
];

export const router = createBrowserRouter([
  // Default route: Welcome page (landing page, no redirect)
  {
    path: '/',
    element: <WelcomePage />,
  },
  // Welcome page alias for backwards compatibility
  {
    path: '/welcome',
    element: <WelcomePage />,
  },

  // ── Demo routes ─────────────────────────────────────────────────────────────
  // Each demo uses the REAL MainLayout + REAL pages, wrapped in DemoModeProvider.
  // The ONLY difference is that DemoModeProvider makes all hooks return mock data.
  //
  // URL pattern: /demo/{cloud}/{page}?namespace={id}&queue={name}
  // The default redirect pre-selects a realistic entity so users land
  // on populated messages immediately.
  {
    path: '/demo/azure',
    element: <DemoAzureLayout />,
    errorElement: <RouteErrorPage />,
    children: [
      {
        index: true,
        element: (
          <Navigate
            to={`/demo/azure/messages?namespace=${DEMO_NAMESPACE_IDS.azure}&queue=orders-queue`}
            replace
          />
        ),
      },
      ...sharedChildren,
    ],
  },
  {
    path: '/demo/aws',
    element: <DemoAwsLayout />,
    errorElement: <RouteErrorPage />,
    children: [
      {
        index: true,
        element: (
          <Navigate
            to={`/demo/aws/messages?namespace=${DEMO_NAMESPACE_IDS.aws}&queue=order-processing`}
            replace
          />
        ),
      },
      ...sharedChildren,
    ],
  },
  {
    path: '/demo/gcp',
    element: <DemoGcpLayout />,
    errorElement: <RouteErrorPage />,
    children: [
      {
        index: true,
        element: (
          <Navigate
            to={`/demo/gcp/messages?namespace=${DEMO_NAMESPACE_IDS.gcp}&topic=lab-results&subscription=results-router-sub`}
            replace
          />
        ),
      },
      ...sharedChildren,
    ],
  },

  // ── Real application ─────────────────────────────────────────────────────────
  // MainLayout with all feature routes — no DemoModeProvider, uses real API.
  // Reuses sharedChildren (same route list the /demo/* trees use) rather than a
  // hand-duplicated copy — a prior hand-duplicated copy had silently dropped the
  // 'fleet' route, so the sidebar's Fleet Operations link 404'd to /welcome.
  {
    path: '/',
    element: <MainLayout />,
    errorElement: <RouteErrorPage />,
    children: sharedChildren,
  },
  // Fallback 404: render a real not-found page inside the app chrome, at the
  // URL the user actually requested — no teleporting to /welcome.
  // Pathless layout route (no `path` key) so it matches regardless of segment count —
  // a splat (path: '*') parent with an index child does NOT render the index child,
  // since a splat consumes the whole remaining path and leaves nothing for an index
  // match to key off. The wildcard must live on the child instead.
  {
    element: <MainLayout />,
    errorElement: <RouteErrorPage />,
    children: [
      {
        path: '*',
        element: <NotFoundPage />,
      },
    ],
  },
]);
