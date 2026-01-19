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
  });
}
