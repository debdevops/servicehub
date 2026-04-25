import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/lib/api/client';
import { ApiError } from '@/lib/api/types';

export interface Subscription {
  name: string;
  activeMessageCount: number;
  deadLetterMessageCount: number;
  topicName: string;
  status: string;
}

export function useSubscriptions(namespaceId: string, topicName: string, autoRefresh: boolean = true) {
  return useQuery({
    queryKey: ['subscriptions', namespaceId, topicName],
    queryFn: async () => {
      const response = await apiClient.get<Subscription[]>(
        `/namespaces/${namespaceId}/topics/${topicName}/subscriptions`,
        { _silent: true }
      );
      return response.data;
    },
    enabled: !!namespaceId && !!topicName,
    staleTime: 2000, // Consider data stale after 2 seconds for immediate count updates
    refetchInterval: autoRefresh ? 7000 : false, // Auto-refresh every 7 seconds when enabled
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
