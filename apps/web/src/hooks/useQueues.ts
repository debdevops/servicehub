import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/lib/api/client';
import { Queue } from '@/lib/api/types';

export function useQueues(namespaceId: string) {
  return useQuery({
    queryKey: ['queues', namespaceId],
    queryFn: async () => {
      const response = await apiClient.get<Queue[]>(`/namespaces/${namespaceId}/queues`);
      return response.data;
    },
    enabled: !!namespaceId,
  });
}
