import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/lib/api/client';

export interface Subscription {
  name: string;
  activeMessageCount: number;
  deadLetterMessageCount: number;
  topicName: string;
  status: string;
}

export function useSubscriptions(namespaceId: string, topicName: string) {
  return useQuery({
    queryKey: ['subscriptions', namespaceId, topicName],
    queryFn: async () => {
      const response = await apiClient.get<Subscription[]>(
        `/namespaces/${namespaceId}/topics/${topicName}/subscriptions`
      );
      return response.data;
    },
    enabled: !!namespaceId && !!topicName,
    staleTime: 2000, // Consider data stale after 2 seconds for immediate count updates
    retry: (failureCount, error: any) => {
      // Don't retry on 404 errors
      if (error?.response?.status === 404) return false;
      return failureCount < 2;
    },
  });
}
