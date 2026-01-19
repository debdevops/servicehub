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
  });
}
