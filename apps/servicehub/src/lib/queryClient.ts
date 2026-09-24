import { QueryClient } from '@tanstack/react-query'

/**
 * One query client for the app.
 *
 * Defaults chosen for an operations tool someone leaves open on a second monitor: data refreshes
 * when the tab regains focus, retries are conservative (a failing cloud call should surface, not be
 * hidden behind four silent retries), and nothing polls by default — a screen that needs live data
 * says so explicitly.
 */
export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: 1,
      refetchOnWindowFocus: true,
      refetchOnReconnect: true,
    },
    mutations: {
      // A replay is not retried automatically. Ever. The user decides.
      retry: 0,
    },
  },
})
