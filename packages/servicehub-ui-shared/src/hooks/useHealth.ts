import { useQuery } from '@tanstack/react-query';
import { healthApi } from '../lib/api/health';
import { useDemoContext } from '../lib/demo/DemoContext';

export function useHealthVersion() {
  const { isDemoMode } = useDemoContext();

  return useQuery({
    queryKey: ['health', 'version'],
    queryFn: healthApi.getVersion,
    enabled: !isDemoMode,
    staleTime: 60_000,
    retry: 1,
  });
}

export function useHealthStatus() {
  const { isDemoMode } = useDemoContext();

  return useQuery({
    queryKey: ['health', 'status'],
    queryFn: healthApi.getStatus,
    enabled: !isDemoMode,
    refetchInterval: (query) => query.state.status === 'error' ? false : 15_000, // Stop on error
    retry: 1,
  });
}

export function useHealthReport() {
  const { isDemoMode } = useDemoContext();

  return useQuery({
    queryKey: ['health', 'report'],
    queryFn: healthApi.getReport,
    enabled: !isDemoMode,
    refetchInterval: (query) => query.state.status === 'error' ? false : 15_000, // Stop on error
    retry: 1,
  });
}
