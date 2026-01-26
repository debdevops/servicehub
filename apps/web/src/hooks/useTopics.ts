import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/lib/api/client';
import { Topic } from '@/lib/api/types';

export function useTopics(namespaceId: string) {
  return useQuery({
    queryKey: ['topics', namespaceId],
    queryFn: async () => {
      const response = await apiClient.get<Topic[]>(`/namespaces/${namespaceId}/topics`);
      return response.data;
    },
    enabled: !!namespaceId,
    staleTime: 10000, // Consider data stale after 10 seconds
    retry: (failureCount, error: any) => {
      // Don't retry on 404 errors
      if (error?.response?.status === 404) return false;
      return failureCount < 2;
    },
  });
}
