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
