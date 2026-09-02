import { createBrowserRouter, Navigate } from 'react-router-dom';
import { lazy, Suspense } from 'react';
// Imported directly from its own module, not the @/pages barrel — importing anything from
// the barrel forces Rollup to evaluate every page it re-exports (including the ones lazily
// imported below), defeating the lazy-loading entirely (Rollup's own
// INEFFECTIVE_DYNAMIC_IMPORT warning surfaces this if the barrel is used here).
import { WelcomePage } from './pages/WelcomePage';
import { RouteErrorPage } from './pages/RouteErrorPage';
import { NotFoundPage } from './pages/NotFoundPage';
import { DEMO_NAMESPACE_IDS } from '@servicehub/ui-shared/lib/demo/mockProviders';

// Lazy-load the layout and its wrappers to defer MainLayout and all its dependencies
// (Header, Sidebar, navigation panels, etc.) from the initial bundle. These comprise
// ~500+ kB and are not needed until the user navigates away from the welcome page.
const AppLayoutLazy = lazy(() => import('./layouts/AppLayouts').then(m => ({ default: m.AppLayout })));
const DemoAzureLayoutLazy = lazy(() => import('./layouts/AppLayouts').then(m => ({ default: m.DemoAzureLayout })));
const DemoAwsLayoutLazy = lazy(() => import('./layouts/AppLayouts').then(m => ({ default: m.DemoAwsLayout })));
const DemoGcpLayoutLazy = lazy(() => import('./layouts/AppLayouts').then(m => ({ default: m.DemoGcpLayout })));

// Lazy-load heavy pages to improve initial bundle size and cold-start performance
const DashboardPageLazy = lazy(() => import('./pages/DashboardPage'));
const HomePageLazy = lazy(() => import('./pages/HomePage').then(m => ({ default: m.HomePage })));
const FleetPageLazy = lazy(() => import('./pages/FleetPage'));
const DlqHistoryPageLazy = lazy(() => import('./pages/DlqHistoryPage'));
const SignatureListPageLazy = lazy(() => import('./pages/SignatureListPage'));
const SignatureDetailsPageLazy = lazy(() => import('./pages/SignatureDetailsPage'));
const FailureIntelligenceCenterPageLazy = lazy(() => import('./pages/FailureIntelligenceCenterPage').then(m => ({ default: m.FailureIntelligenceCenterPage })));
const IncidentWorkspacePageLazy = lazy(() => import('./pages/IncidentWorkspacePage').then(m => ({ default: m.IncidentWorkspacePage })));
const CloudBridgePageLazy = lazy(() => import('./pages/CloudBridgePage').then(m => ({ default: m.CloudBridgePage })));
const CrossCloudTracePageLazy = lazy(() => import('./pages/CrossCloudTracePage').then(m => ({ default: m.CrossCloudTracePage })));
const AuditPageLazy = lazy(() => import('./pages/AuditPage').then(m => ({ default: m.AuditPage })));
const RecoveryLedgerPageLazy = lazy(() => import('./pages/RecoveryLedgerPage'));
const PlaybookLedgerPageLazy = lazy(() => import('./pages/PlaybookLedgerPage'));
const GovernanceGrantsPageLazy = lazy(() => import('./pages/GovernanceGrantsPage'));
const RecoveryAgeingPageLazy = lazy(() => import('./pages/RecoveryAgeingPage'));
const RecoveryOperationDetailPageLazy = lazy(() => import('./pages/RecoveryOperationDetailPage'));
const MessagesOverviewPageLazy = lazy(() => import('./pages/MessagesOverviewPage'));
const MessagesPageLazy = lazy(() => import('./pages/MessagesPage').then(m => ({ default: m.MessagesPage })));
const LiveTailPageLazy = lazy(() => import('./pages/LiveTailPage').then(m => ({ default: m.LiveTailPage })));
const ConnectPageLazy = lazy(() => import('./pages/ConnectPage').then(m => ({ default: m.ConnectPage })));
const RulesPageLazy = lazy(() => import('./pages/RulesPage').then(m => ({ default: m.RulesPage })));
const ApprovalQueuePageLazy = lazy(() => import('./pages/ApprovalQueuePage'));
const AutonomyPageLazy = lazy(() => import('./pages/AutonomyPage'));
const ProactiveInsightsPageLazy = lazy(() => import('./pages/ProactiveInsightsPage'));
const HealthPageLazy = lazy(() => import('./pages/HealthPage').then(m => ({ default: m.HealthPage })));
const HelpPageLazy = lazy(() => import('./pages/HelpPage').then(m => ({ default: m.HelpPage })));
const AdvancedServiceHubPageLazy = lazy(() => import('./pages/AdvancedServiceHubPage').then(m => ({ default: m.AdvancedServiceHubPage })));
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

// Layout loading fallback — shown while MainLayout and its dependencies load
// eslint-disable-next-line react-refresh/only-export-components
function LayoutLoading() {
  return (
    <div className="h-screen flex items-center justify-center bg-gray-50">
      <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-600" />
    </div>
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
    path: 'live-tail',
    element: (
      <Suspense fallback={<PageLoading />}>
        <LiveTailPageLazy />
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
    path: 'approval-queue',
    element: (
      <Suspense fallback={<PageLoading />}>
        <ApprovalQueuePageLazy />
      </Suspense>
    ),
  },
  {
    path: 'autonomy',
    element: (
      <Suspense fallback={<PageLoading />}>
        <AutonomyPageLazy />
      </Suspense>
    ),
  },
  // Old route, renamed to '/autonomy' as part of the Advanced ServiceHub redesign — kept as a
  // redirect so existing bookmarks and the browser back-button history don't 404.
  {
    path: 'autonomy-dashboard',
    element: <Navigate to="../autonomy" replace />,
  },
  {
    path: 'insights',
    element: (
      <Suspense fallback={<PageLoading />}>
        <ProactiveInsightsPageLazy />
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
    path: 'advanced-servicehub',
    element: (
      <Suspense fallback={<PageLoading />}>
        <AdvancedServiceHubPageLazy />
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
    path: 'home',
    element: (
      <Suspense fallback={<PageLoading />}>
        <HomePageLazy />
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
    path: 'signatures',
    element: (
      <Suspense fallback={<PageLoading />}>
        <SignatureListPageLazy />
      </Suspense>
    ),
  },
  {
    path: 'signatures/:signatureHash',
    element: (
      <Suspense fallback={<PageLoading />}>
        <SignatureDetailsPageLazy />
      </Suspense>
    ),
  },
  {
    path: 'incidents',
    element: (
      <Suspense fallback={<PageLoading />}>
        <FailureIntelligenceCenterPageLazy />
      </Suspense>
    ),
  },
  {
    path: 'incidents/:signatureHash',
    element: (
      <Suspense fallback={<PageLoading />}>
        <IncidentWorkspacePageLazy />
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
  {
    path: 'recovery',
    element: (
      <Suspense fallback={<PageLoading />}>
        <RecoveryLedgerPageLazy />
      </Suspense>
    ),
  },
  {
    path: 'recovery/ageing',
    element: (
      <Suspense fallback={<PageLoading />}>
        <RecoveryAgeingPageLazy />
      </Suspense>
    ),
  },
  {
    path: 'recovery/:operationId',
    element: (
      <Suspense fallback={<PageLoading />}>
        <RecoveryOperationDetailPageLazy />
      </Suspense>
    ),
  },
  {
    path: 'playbook',
    element: (
      <Suspense fallback={<PageLoading />}>
        <PlaybookLedgerPageLazy />
      </Suspense>
    ),
  },
  {
    path: 'governance',
    element: (
      <Suspense fallback={<PageLoading />}>
        <GovernanceGrantsPageLazy />
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
  // Layouts are lazy-loaded to defer MainLayout and its dependencies from initial bundle.
  //
  // URL pattern: /demo/{cloud}/{page}?namespace={id}&queue={name}
  // The default redirect pre-selects a realistic entity so users land
  // on populated messages immediately.
  {
    path: '/demo/azure',
    element: (
      <Suspense fallback={<LayoutLoading />}>
        <DemoAzureLayoutLazy />
      </Suspense>
    ),
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
    element: (
      <Suspense fallback={<LayoutLoading />}>
        <DemoAwsLayoutLazy />
      </Suspense>
    ),
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
    element: (
      <Suspense fallback={<LayoutLoading />}>
        <DemoGcpLayoutLazy />
      </Suspense>
    ),
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
  // Layout is lazy-loaded to defer MainLayout dependencies from initial bundle.
  {
    path: '/',
    element: (
      <Suspense fallback={<LayoutLoading />}>
        <AppLayoutLazy />
      </Suspense>
    ),
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
    element: (
      <Suspense fallback={<LayoutLoading />}>
        <AppLayoutLazy />
      </Suspense>
    ),
    errorElement: <RouteErrorPage />,
    children: [
      {
        path: '*',
        element: <NotFoundPage />,
      },
    ],
  },
]);
