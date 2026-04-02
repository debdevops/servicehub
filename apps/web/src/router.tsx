import { createBrowserRouter, Navigate } from 'react-router-dom';
import { MainLayout } from '@/components/layout';
import { DashboardPage, CorrelationExplorerPage, MessagesPage, ConnectPage, DlqHistoryPage, RulesPage, HealthPage, HelpPage, ScheduledMessagesPage } from '@/pages';

export const router = createBrowserRouter([
  {
    path: '/',
    element: <MainLayout />,
    children: [
      {
        index: true,
        element: <Navigate to="/connect" replace />,
      },
      {
        path: 'dashboard',
        element: <DashboardPage />,
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
        element: <DlqHistoryPage />,
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
        element: <CorrelationExplorerPage />,
      },
      {
        path: '*',
        element: <Navigate to="/connect" replace />,
      },
    ],
  },
]);
