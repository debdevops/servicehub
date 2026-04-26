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

// Lazy-load heavy pages to improve initial bundle size and cold-start performance
const DashboardPageLazy = lazy(() => import('./pages/DashboardPage'));
const DlqHistoryPageLazy = lazy(() => import('./pages/DlqHistoryPage'));
const CorrelationExplorerPageLazy = lazy(() => import('./pages/CorrelationExplorerPage'));
const InsightsPageLazy = lazy(() => import('./pages/InsightsPage').then(m => ({ default: m.InsightsPage })));

// Loading fallback component (co-located here intentionally — used only by router)
// eslint-disable-next-line react-refresh/only-export-components
function PageLoading() {
  return (
    <div className="flex items-center justify-center h-full bg-gray-50">
      <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-600" />
    </div>
  );
}

export const router = createBrowserRouter([
  {
    path: '/',
    element: <WelcomePage />,
  },
  {
    path: '/welcome',
    element: <WelcomePage />,
  },
  {
    path: '/',
    element: <MainLayout />,
    errorElement: <Navigate to="/" replace />,
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
        path: 'insights',
        element: (
          <Suspense fallback={<PageLoading />}>
            <InsightsPageLazy />
          </Suspense>
        ),
      },
      {
        index: true,
        element: <Navigate to="/connect" replace />,
      },
    ],
  },
  {
    // Fallback 404: redirect unknown paths back to welcome
    path: '*',
    element: <Navigate to="/" replace />,
  },
]);
