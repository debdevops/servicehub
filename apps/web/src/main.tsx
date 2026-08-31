import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { RouterProvider } from 'react-router-dom';
import { QueryClientProvider } from '@tanstack/react-query';
import { Toaster, ToastBar, toast } from 'react-hot-toast';
import { AppInsightsContext } from '@microsoft/applicationinsights-react-js';
import { router } from './router';
import { queryClient } from '@servicehub/ui-shared/lib/queryClient';
import { reactPlugin } from '@servicehub/ui-shared/lib/telemetry';
import { ActiveJobsProvider } from '@servicehub/ui-shared/lib/activeJobs/ActiveJobsContext';
import { ErrorBoundary } from './components/ErrorBoundary';
import './styles/index.css';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AppInsightsContext.Provider value={reactPlugin}>
    <ErrorBoundary>
      <QueryClientProvider client={queryClient}>
        <ActiveJobsProvider>
          <RouterProvider router={router} />
        </ActiveJobsProvider>
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
          // Errors carry the server's explanation and are worth reading, so they linger
          // longer than the 3s default — but never indefinitely, and always dismissible
          // via the button below.
          duration: 8000,
          iconTheme: {
            primary: '#EF4444',
            secondary: '#fff',
          },
        },
      }}
    >
      {/* react-hot-toast does not dismiss on click by default. Error toasts get an explicit
          close button so a user can clear a long provider message without waiting it out. */}
      {(t) => (
        <ToastBar toast={t}>
          {({ icon, message }) => (
            <>
              {icon}
              {message}
              {t.type === 'error' && (
                <button
                  type="button"
                  onClick={() => toast.dismiss(t.id)}
                  aria-label="Dismiss error"
                  className="ml-2 shrink-0 self-start rounded px-1.5 py-0.5 text-lg leading-none text-gray-400 hover:bg-gray-100 hover:text-gray-700 focus:outline-none focus:ring-2 focus:ring-red-400"
                >
                  ×
                </button>
              )}
            </>
          )}
        </ToastBar>
      )}
    </Toaster>
      </QueryClientProvider>
    </ErrorBoundary>
    </AppInsightsContext.Provider>
  </StrictMode>,
);
