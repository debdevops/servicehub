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
    staleTime: 15_000,
    refetchInterval: autoRefresh ? 30_000 : false,
    refetchIntervalInBackground: false,
    retry: (failureCount, error: ApiError) => {
      if (error?.response?.status === 404) return false;
      if (error?.response?.status === 429) return false;
      if ((error?.response?.status ?? 0) >= 500) return false;
      return failureCount < 2;
    },
  });
}
