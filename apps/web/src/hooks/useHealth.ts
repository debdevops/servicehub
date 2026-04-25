import { useQuery } from '@tanstack/react-query';
import { healthApi } from '@/lib/api/health';

export function useHealthVersion() {
  return useQuery({
    queryKey: ['health', 'version'],
    queryFn: healthApi.getVersion,
    staleTime: 60_000,
    retry: 1,
  });
}

export function useHealthStatus() {
  return useQuery({
    queryKey: ['health', 'status'],
    queryFn: healthApi.getStatus,
    refetchInterval: (query) => query.state.status === 'error' ? false : 15_000, // Stop on error
    retry: 1,
  });
}
