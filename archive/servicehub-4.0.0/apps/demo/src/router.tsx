import { createBrowserRouter } from 'react-router-dom';
import { DemoLayout } from './components/layout/DemoLayout';
import { DemoHomePage } from './pages/DemoHomePage';
import { DashboardPage } from './pages/DashboardPage';
import { MessagesPage } from './pages/MessagesPage';
import { DlqPage } from './pages/DlqPage';

export const router = createBrowserRouter([
  {
    path: '/',
    element: <DemoLayout />,
    children: [
      {
        index: true,
        element: <DemoHomePage />,
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
        path: 'dlq',
        element: <DlqPage />,
      },
    ],
  },
]);
