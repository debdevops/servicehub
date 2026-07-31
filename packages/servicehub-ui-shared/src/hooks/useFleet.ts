import { useQuery } from '@tanstack/react-query';
import { fleetApi } from '../lib/api/fleet';
import { useDemoContext } from '../lib/demo/DemoContext';

/**
 * Hook for the cross-namespace fleet operations overview — the "what died overnight across
 * everything?" dashboard. Polls periodically so it works as an always-open operations tab.
 */
export function useFleetOverview(windowHours = 24, enabled = true) {
  const { isDemoMode } = useDemoContext();

  return useQuery({
    queryKey: ['fleet-overview', windowHours],
    queryFn: () => fleetApi.getOverview(windowHours),
    enabled: !isDemoMode && enabled,
    staleTime: 15_000,
    refetchInterval: 30_000,
    refetchIntervalInBackground: false,
    retry: (failureCount, error: unknown) => {
      const err = error as { response?: { status?: number } };
      if (err?.response?.status === 429) return false;
      return failureCount < 2;
    },
  });
}
