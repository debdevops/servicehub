import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/lib/api/client';
import { Topic } from '@/lib/api/types';

export function useTopics(namespaceId: string, autoRefresh: boolean = true) {
  return useQuery({
    queryKey: ['topics', namespaceId],
    queryFn: async () => {
      const response = await apiClient.get<Topic[]>(`/namespaces/${namespaceId}/topics`);
      return response.data;
    },
    enabled: !!namespaceId,
    staleTime: 2000, // Consider data stale after 2 seconds for immediate updates
    refetchInterval: autoRefresh ? 7000 : false, // Auto-refresh every 7 seconds when enabled
    refetchIntervalInBackground: false, // Don't refetch when tab is not visible
    retry: (failureCount, error: any) => {
      // Don't retry on 404 errors
      if (error?.response?.status === 404) return false;
      return failureCount < 2;
    },
  });
}
