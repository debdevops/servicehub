import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { RouterProvider } from 'react-router-dom';
import { QueryClientProvider } from '@tanstack/react-query';
import { Toaster } from 'react-hot-toast';
import { AppInsightsContext } from '@microsoft/applicationinsights-react-js';
import { router } from './router';
import { queryClient } from './lib/queryClient';
import { reactPlugin } from './lib/telemetry';
import { ErrorBoundary } from './components/ErrorBoundary';
import { WelcomeDialog } from './components/WelcomeDialog';
import './styles/index.css';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AppInsightsContext.Provider value={reactPlugin}>
    <ErrorBoundary>
      <QueryClientProvider client={queryClient}>
        <WelcomeDialog />
        <RouterProvider router={router} />
      <Toaster
      position="top-right"
      toastOptions={{
        duration: 3000,
        style: {
          background: '#fff',
          color: '#374151',
          border: '1px solid #E5E7EB',
          padding: '12px 16px',
          borderRadius: '8px',
          boxShadow: '0 4px 12px rgba(0, 0, 0, 0.1)',
        },
        success: {
          iconTheme: {
            primary: '#10B981',
            secondary: '#fff',
          },
        },
        error: {
          iconTheme: {
            primary: '#EF4444',
            secondary: '#fff',
          },
        },
      }}
    />
      </QueryClientProvider>
    </ErrorBoundary>
    </AppInsightsContext.Provider>
  </StrictMode>,
);
