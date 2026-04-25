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
    if (error?.response?.status === 404) return false;
    if (error?.response?.status === 429) return false;
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
  totalTopics: number;
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

  // Also fetch stats (with subscription DLQs) for each namespace
  const statsResults = useQueries({
    queries: namespaceIds.map((id) => ({
      queryKey: ['namespace-stats', id] as const,
      queryFn: async () => {
        const response = await apiClient.get<{
          totalQueues: number;
          totalTopics: number;
          totalSubscriptions: number;
          totalActive: number;
          totalDlq: number;
          totalScheduled: number;
        }>(`/namespaces/${id}/stats`, { _silent: true });
        return response.data;
      },
      enabled: !!id,
      staleTime: 15_000,
      refetchInterval: autoRefresh ? 30_000 : (false as const),
      refetchIntervalInBackground: false,
      retry: (failureCount: number, error: ApiError) => {
        if (error?.response?.status === 404) return false;
        if (error?.response?.status === 429) return false;
        if ((error?.response?.status ?? 0) >= 500) return false;
        return failureCount < 2;
      },
    })),
  });

  return results.map((result, i) => {
    const queues = result.data;
    const stats = statsResults[i]?.data;
    return {
      namespaceId: namespaceIds[i],
      queues,
      totalActive: stats?.totalActive ?? queues?.reduce((s, q) => s + q.activeMessageCount, 0) ?? 0,
      totalDlq: stats?.totalDlq ?? queues?.reduce((s, q) => s + q.deadLetterMessageCount, 0) ?? 0,
      totalScheduled: stats?.totalScheduled ?? queues?.reduce((s, q) => s + q.scheduledMessageCount, 0) ?? 0,
      totalQueues: stats?.totalQueues ?? queues?.length ?? 0,
      totalTopics: stats?.totalTopics ?? 0,
      isLoading: result.isLoading || (statsResults[i]?.isLoading ?? false),
      isError: result.isError,
    };
  });
}
