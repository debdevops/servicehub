import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/lib/api/client';
import { Topic, ApiError } from '@/lib/api/types';

export function useTopics(namespaceId: string, autoRefresh: boolean = true) {
  return useQuery({
    queryKey: ['topics', namespaceId],
    queryFn: async () => {
      const response = await apiClient.get<Topic[]>(`/namespaces/${namespaceId}/topics`, {
        _silent: true,
      });
      return response.data;
    },
    enabled: !!namespaceId,
    staleTime: 2000, // Consider data stale after 2 seconds for immediate updates
    refetchInterval: (query) => query.state.status === 'error' ? false : (autoRefresh ? 7000 : false), // Stop on error to prevent 429 storms
    refetchIntervalInBackground: false, // Don't refetch when tab is not visible
    retry: (failureCount, error: ApiError) => {
      // Don't retry on 404 or Service Bus connectivity errors
      if (error?.response?.status === 404) return false;
      if (error?.response?.status === 429) return false;
      if ((error?.response?.status ?? 0) >= 500) return false;
      return failureCount < 2;
    },
  });
}
