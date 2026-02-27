import { createBrowserRouter, Navigate } from 'react-router-dom';
import { MainLayout } from '@/components/layout';
import { MessagesPage, ConnectPage, DlqHistoryPage, RulesPage } from '@/pages';

export const router = createBrowserRouter([
  {
    path: '/',
    element: <MainLayout />,
    children: [
      {
        index: true,
        element: <Navigate to="/messages" replace />,
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
    ],
  },
]);
