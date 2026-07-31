import { createBrowserRouter } from 'react-router-dom';
import { SandboxLayout } from './components/layout/SandboxLayout';
import { SandboxHomePage } from './pages/SandboxHomePage';
import { AboutSandboxPage } from './pages/AboutSandboxPage';
import { ComingSoonPage } from './pages/ComingSoonPage';
import { NamespacesPage } from './pages/NamespacesPage';
import { QueuesPage } from './pages/QueuesPage';
import { TopicsPage } from './pages/TopicsPage';
import { SubscriptionsPage } from './pages/SubscriptionsPage';

export const router = createBrowserRouter([
  {
    path: '/',
    element: <SandboxLayout />,
    children: [
      {
        index: true,
        element: <SandboxHomePage />,
      },
      {
        path: 'namespaces',
        element: <NamespacesPage />,
      },
      {
        path: 'queues',
        element: <QueuesPage />,
      },
      {
        path: 'topics',
        element: <TopicsPage />,
      },
      {
        path: 'topics/:topicName',
        element: <SubscriptionsPage />,
      },
      {
        path: 'about',
        element: <AboutSandboxPage />,
      },
      {
        path: 'coming-soon',
        element: <ComingSoonPage />,
      },
    ],
  },
]);
