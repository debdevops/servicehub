import { useQuery, useQueries, UseQueryOptions } from '@tanstack/react-query';
import { apiClient } from '@/lib/api/client';
import { Queue, ApiError } from '@/lib/api/types';
import { useDemoContext } from '@/lib/demo/DemoContext';
import { getMockQueues } from '@/lib/demo/mockProviders';

const queuesQueryOptions = (
  namespaceId: string,
  autoRefresh: boolean,
  refetchMs: number = 30_000,
): UseQueryOptions<Queue[], ApiError> => ({
  queryKey: ['queues', namespaceId] as const,
  queryFn: async (): Promise<Queue[]> => {
    const response = await apiClient.get<Queue[]>(`/namespaces/${namespaceId}/queues`, {
      _silent: true,
    });
    return response.data;
  },
  enabled: !!namespaceId,
  staleTime: 15_000,
  refetchInterval: autoRefresh ? refetchMs : (false as const),
  refetchIntervalInBackground: false,
  retry: (failureCount: number, error: ApiError) => {
    if (error?.response?.status === 404) return false;
    if (error?.response?.status === 429) return false;
    if ((error?.response?.status ?? 0) >= 500) return false;
    return failureCount < 2;
  },
});

export function useQueues(namespaceId: string, autoRefresh: boolean = true, refetchMs: number = 30_000) {
  const { isDemoMode, cloudProvider } = useDemoContext();

  // Compute query options once — both branches return Queue[] so the
  // return type is always UseQueryResult<Queue[], ApiError>.
  const options: UseQueryOptions<Queue[], ApiError> = isDemoMode && cloudProvider
    ? {
        queryKey: ['queues', 'demo', cloudProvider],
        queryFn: (): Promise<Queue[]> => Promise.resolve(getMockQueues(cloudProvider)),
        staleTime: Infinity,
        enabled: true,
        refetchInterval: false,
        refetchIntervalInBackground: false,
        retry: false,
      }
    : queuesQueryOptions(namespaceId, autoRefresh, refetchMs);

  return useQuery(options);
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

export interface NamespaceStatsData {
  totalQueues: number;
  totalTopics: number;
  totalSubscriptions: number;
  totalActive: number;
  totalDlq: number;
  totalScheduled: number;
}

export interface NamespaceStatsResult {
  namespaceId: string;
  data: NamespaceStatsData | undefined;
  isLoading: boolean;
  isError: boolean;
}

/**
 * Fetches `/namespaces/{id}/stats` (queue/topic/subscription/message-count rollup) for many
 * namespaces in parallel, sharing the `['namespace-stats', id]` query cache with every other
 * consumer — the same cached query `Header`, `QuickAccessPanel`, `CloudBridgePage`, and
 * `useAllNamespacesQueues` all warm, so calling this from multiple places adds no extra
 * network cost. Centralizing it here (rather than each caller hand-rolling its own
 * `useQueries` block) keeps the retry-suppression policy consistent everywhere.
 */
export function useNamespaceStats(
  namespaceIds: string[],
  autoRefresh: boolean = true,
  refetchMs: number = 60_000,
): NamespaceStatsResult[] {
  const statsResults = useQueries({
    queries: namespaceIds.map((id) => ({
      queryKey: ['namespace-stats', id] as const,
      queryFn: async (): Promise<NamespaceStatsData> => {
        const response = await apiClient.get<NamespaceStatsData>(`/namespaces/${id}/stats`, {
          _silent: true,
        });
        return response.data;
      },
      enabled: !!id,
      staleTime: 30_000,
      refetchInterval: autoRefresh ? refetchMs : (false as const),
      refetchIntervalInBackground: false,
      retry: (failureCount: number, error: ApiError) => {
        if (error?.response?.status === 404) return false;
        if (error?.response?.status === 429) return false;
        if ((error?.response?.status ?? 0) >= 500) return false;
        return failureCount < 2;
      },
    })),
  });

  return statsResults.map((result, i) => ({
    namespaceId: namespaceIds[i],
    data: result.data,
    isLoading: result.isLoading,
    isError: result.isError,
  }));
}

/**
 * Fetches queue data for multiple namespaces in parallel using shared query cache.
 * Cards using useQueues() will hit the same cache — no duplicate requests.
 */
export function useAllNamespacesQueues(
  namespaceIds: string[],
  autoRefresh: boolean = true,
  intervals?: { queuesMs?: number; statsMs?: number },
): NamespaceQueueStats[] {
  const results = useQueries({
    queries: namespaceIds.map((id) => queuesQueryOptions(id, autoRefresh, intervals?.queuesMs)),
  });

  // Also fetch stats (with subscription DLQs) for each namespace
  const statsResults = useNamespaceStats(namespaceIds, autoRefresh, intervals?.statsMs ?? 60_000);

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
