import { useQuery, useQueries } from '@tanstack/react-query';
import { apiClient } from '@/lib/api/client';
import { Queue, ApiError } from '@/lib/api/types';

const queuesQueryOptions = (namespaceId: string, autoRefresh: boolean) => ({
  queryKey: ['queues', namespaceId] as const,
  queryFn: async () => {
    const response = await apiClient.get<Queue[]>(`/namespaces/${namespaceId}/queues`, {
      _silent: true,
    });
    return response.data;
  },
  enabled: !!namespaceId,
  staleTime: 2000,
  refetchInterval: autoRefresh ? 7000 : (false as const),
  refetchIntervalInBackground: false,
  retry: (failureCount: number, error: ApiError) => {
    if (error?.response?.status === 400) return false;
    if (error?.response?.status === 404) return false;
    if ((error?.response?.status ?? 0) >= 500) return false;
    return failureCount < 2;
  },
});

export function useQueues(namespaceId: string, autoRefresh: boolean = true) {
  return useQuery(queuesQueryOptions(namespaceId, autoRefresh));
}

export interface NamespaceQueueStats {
  namespaceId: string;
  queues: Queue[] | undefined;
  totalActive: number;
  totalDlq: number;
  totalScheduled: number;
  totalQueues: number;
  isLoading: boolean;
  isError: boolean;
}

/**
 * Fetches queue data for multiple namespaces in parallel using shared query cache.
 * Cards using useQueues() will hit the same cache — no duplicate requests.
 */
export function useAllNamespacesQueues(
  namespaceIds: string[],
  autoRefresh: boolean = true,
): NamespaceQueueStats[] {
  const results = useQueries({
    queries: namespaceIds.map((id) => queuesQueryOptions(id, autoRefresh)),
  });

  return results.map((result, i) => {
    const queues = result.data;
    return {
      namespaceId: namespaceIds[i],
      queues,
      totalActive: queues?.reduce((s, q) => s + q.activeMessageCount, 0) ?? 0,
      totalDlq: queues?.reduce((s, q) => s + q.deadLetterMessageCount, 0) ?? 0,
      totalScheduled: queues?.reduce((s, q) => s + q.scheduledMessageCount, 0) ?? 0,
      totalQueues: queues?.length ?? 0,
      isLoading: result.isLoading,
      isError: result.isError,
    };
  });
}
