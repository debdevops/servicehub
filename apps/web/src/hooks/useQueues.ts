import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/lib/api/client';
import { Queue } from '@/lib/api/types';

export function useQueues(namespaceId: string, autoRefresh: boolean = true) {
  return useQuery({
    queryKey: ['queues', namespaceId],
    queryFn: async () => {
      const response = await apiClient.get<Queue[]>(`/namespaces/${namespaceId}/queues`);
      return response.data;
    },
    enabled: !!namespaceId,
    staleTime: 2000, // Consider data stale after 2 seconds for immediate count updates
    refetchInterval: autoRefresh ? 7000 : false, // Auto-refresh every 7 seconds when enabled
    refetchIntervalInBackground: false, // Don't refetch when tab is not visible
    retry: (failureCount, error: any) => {
      // Don't retry on 404 errors
      if (error?.response?.status === 404) return false;
      return failureCount < 2;
    },
  });
}
