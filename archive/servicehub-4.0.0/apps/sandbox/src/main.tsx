import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { RouterProvider } from 'react-router-dom';
import { QueryClientProvider } from '@tanstack/react-query';
import { Toaster } from 'react-hot-toast';
import { AppInsightsContext } from '@microsoft/applicationinsights-react-js';
import { router } from './router';
import { queryClient } from '@servicehub/ui-shared/lib/queryClient';
import { reactPlugin } from '@servicehub/ui-shared/lib/telemetry';
import { ErrorBoundary } from './components/ErrorBoundary';
import { SandboxDataProvider } from './providers/SandboxDataProvider';
import './styles/index.css';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AppInsightsContext.Provider value={reactPlugin}>
      <ErrorBoundary>
        <QueryClientProvider client={queryClient}>
          <SandboxDataProvider>
            <RouterProvider router={router} />
            <Toaster position="top-right" toastOptions={{ duration: 3000 }} />
          </SandboxDataProvider>
        </QueryClientProvider>
      </ErrorBoundary>
    </AppInsightsContext.Provider>
  </StrictMode>,
);
