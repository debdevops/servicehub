import { createBrowserRouter, Navigate } from 'react-router-dom';
import { lazy, Suspense } from 'react';
import { MainLayout } from '@/components/layout';
import {
  MessagesPage,
  ConnectPage,
  RulesPage,
  HealthPage,
  HelpPage,
  ScheduledMessagesPage,
  SecurityPage,
  WelcomePage,
} from '@/pages';
import { DemoModeProvider } from '@/lib/demo/DemoContext';
import { DEMO_NAMESPACE_IDS } from '@/lib/demo/mockProviders';

// Lazy-load heavy pages to improve initial bundle size and cold-start performance
const DashboardPageLazy = lazy(() => import('./pages/DashboardPage'));
const DlqHistoryPageLazy = lazy(() => import('./pages/DlqHistoryPage'));
const CorrelationExplorerPageLazy = lazy(() => import('./pages/CorrelationExplorerPage'));
const InsightsPageLazy = lazy(() => import('./pages/InsightsPage').then(m => ({ default: m.InsightsPage })));
const CloudBridgePageLazy = lazy(() => import('./pages/CloudBridgePage').then(m => ({ default: m.CloudBridgePage })));
const SimulatorPageLazy = lazy(() => import('./pages/SimulatorPage').then(m => ({ default: m.SimulatorPage })));
const CrossCloudTracePageLazy = lazy(() => import('./pages/CrossCloudTracePage').then(m => ({ default: m.CrossCloudTracePage })));

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
  { path: 'messages', element: <MessagesPage /> },
  { path: 'connect', element: <ConnectPage /> },
  { path: 'rules', element: <RulesPage /> },
  { path: 'health', element: <HealthPage /> },
  { path: 'help', element: <HelpPage /> },
  { path: 'scheduled', element: <ScheduledMessagesPage /> },
  { path: 'security', element: <SecurityPage /> },
  {
    path: 'dashboard',
    element: (
      <Suspense fallback={<PageLoading />}>
        <DashboardPageLazy />
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
    path: 'correlation',
    element: (
      <Suspense fallback={<PageLoading />}>
        <CorrelationExplorerPageLazy />
      </Suspense>
    ),
  },
  {
    path: 'insights',
    element: (
      <Suspense fallback={<PageLoading />}>
        <InsightsPageLazy />
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
    path: 'simulator',
    element: (
      <Suspense fallback={<PageLoading />}>
        <SimulatorPageLazy />
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
    errorElement: <Navigate to="/demo/azure" replace />,
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
    errorElement: <Navigate to="/demo/aws" replace />,
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
    errorElement: <Navigate to="/demo/gcp" replace />,
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
  // MainLayout with all feature routes — no DemoModeProvider, uses real API
  {
    path: '/',
    element: <MainLayout />,
    errorElement: <Navigate to="/welcome" replace />,
    children: [
      {
        path: 'dashboard',
        element: (
          <Suspense fallback={<PageLoading />}>
            <DashboardPageLazy />
          </Suspense>
        ),
      },
      {
        path: 'messages',
        element: <MessagesPage />,
      },
      {
        path: 'connect',
        element: <ConnectPage />,
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
        path: 'rules',
        element: <RulesPage />,
      },
      {
        path: 'health',
        element: <HealthPage />,
      },
      {
        path: 'help',
        element: <HelpPage />,
      },
      {
        path: 'scheduled',
        element: <ScheduledMessagesPage />,
      },
      {
        path: 'correlation',
        element: (
          <Suspense fallback={<PageLoading />}>
            <CorrelationExplorerPageLazy />
          </Suspense>
        ),
      },
      {
        path: 'security',
        element: <SecurityPage />,
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
        path: 'simulator',
        element: (
          <Suspense fallback={<PageLoading />}>
            <SimulatorPageLazy />
          </Suspense>
        ),
      },
      {
        path: 'insights',
        element: (
          <Suspense fallback={<PageLoading />}>
            <InsightsPageLazy />
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
    ],
  },
  // Fallback 404: redirect unknown paths to welcome
  {
    path: '*',
    element: <Navigate to="/welcome" replace />,
  },
]);
