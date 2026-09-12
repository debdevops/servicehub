import { useQuery, useQueries, UseQueryOptions } from '@tanstack/react-query';
import { apiClient } from '../lib/api/client';
import { Queue, ApiError } from '../lib/api/types';
import { useDemoContext } from '../lib/demo/DemoContext';
import { getMockQueues, getMockStats, getMockTopics } from '../lib/demo/mockProviders';

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
  /** Every topic name in the namespace — entity-picker use cases (e.g. RulesPage's scope validation). */
  topicNames: string[] | undefined;
  totalActive: number;
  totalDlq: number;
  totalScheduled: number;
  totalQueues: number;
  totalTopics: number;
  totalSubscriptions: number;
  /** When this namespace's queue list last settled (client fetch time — a freshness signal
   * for "last updated" displays, not a server-side scan timestamp). Undefined until the
   * first successful fetch. */
  dataUpdatedAt: number | undefined;
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
  /** Every queue name in the namespace — entity-picker use cases (e.g. RulesPage's scope validation). */
  queueNames: string[];
  /** Every topic name in the namespace — entity-picker use cases (e.g. RulesPage's scope validation). */
  topicNames: string[];
}

export interface NamespaceStatsResult {
  namespaceId: string;
  data: NamespaceStatsData | undefined;
  dataUpdatedAt: number | undefined;
  isLoading: boolean;
  isError: boolean;
}

interface NamespaceStatsBatchEntry {
  namespaceId: string;
  stats: Omit<NamespaceStatsData, 'queueNames' | 'topicNames'>;
  queueNames: string[];
  topicNames: string[];
}

/**
 * Fetches queue/topic/subscription/message-count rollups for many namespaces in ONE request
 * to `POST /namespaces/stats/batch`, sharing the `['namespace-stats-batch', ids]` query cache
 * with every other consumer requesting the same namespace set — `Header`, `QuickAccessPanel`,
 * `CloudBridgePage`, and `useAllNamespacesQueues` all warm it, so calling this from multiple
 * places adds no extra network cost.
 *
 * Fleet-scale finding: this used to be `N` parallel `GET .../stats` requests (one `useQueries`
 * entry per namespace). `Header` and `QuickAccessPanel` call this hook with the *full* namespace
 * list on effectively every page, so at 30+ namespaces every page load fired 30+ concurrent live
 * provider round-trips — enough to starve the connection pool and delay unrelated requests
 * (observed: proxy timeouts under a 36-namespace fleet). The backend computes all of them with
 * its own bounded concurrency (`NamespacesController.GetStatsBatch`, mirroring
 * `ServiceBusHealthCheck`'s pattern) — bounding it server-side, not client-side, because the
 * provider connection pool is shared across every concurrent caller/operator, not just one
 * browser tab.
 */
export function useNamespaceStats(
  namespaceIds: string[],
  autoRefresh: boolean = true,
  refetchMs: number = 60_000,
): NamespaceStatsResult[] {
  const { isDemoMode, cloudProvider } = useDemoContext();

  // Demo mode never touches the network — mock stats resolve instantly and per-namespace, so
  // there's no fan-out to collapse; keep it as independent per-id queries.
  const demoResults = useQueries({
    queries: isDemoMode && cloudProvider
      ? namespaceIds.map((id) => ({
          queryKey: ['namespace-stats', 'demo', cloudProvider, id] as const,
          queryFn: (): Promise<NamespaceStatsData> =>
            Promise.resolve({
              ...getMockStats(cloudProvider),
              queueNames: getMockQueues(cloudProvider).map((q) => q.name),
              topicNames: getMockTopics(cloudProvider).map((t) => t.name),
            }),
          staleTime: Infinity,
        }))
      : [],
  });

  const sortedIds = [...namespaceIds].sort();

  const batchQuery = useQuery({
    queryKey: ['namespace-stats-batch', sortedIds] as const,
    queryFn: async (): Promise<Record<string, NamespaceStatsData>> => {
      const response = await apiClient.post<NamespaceStatsBatchEntry[]>(
        '/namespaces/stats/batch',
        { namespaceIds: sortedIds },
        { _silent: true },
      );
      const byId: Record<string, NamespaceStatsData> = {};
      for (const entry of response.data) {
        byId[entry.namespaceId] = { ...entry.stats, queueNames: entry.queueNames, topicNames: entry.topicNames };
      }
      return byId;
    },
    enabled: !(isDemoMode && cloudProvider) && sortedIds.length > 0,
    staleTime: 30_000,
    refetchInterval: autoRefresh ? refetchMs : (false as const),
    refetchIntervalInBackground: false,
    retry: (failureCount: number, error: ApiError) => {
      if (error?.response?.status === 404) return false;
      if (error?.response?.status === 429) return false;
      if ((error?.response?.status ?? 0) >= 500) return false;
      return failureCount < 2;
    },
  });

  if (isDemoMode && cloudProvider) {
    return demoResults.map((result, i) => ({
      namespaceId: namespaceIds[i],
      data: result.data,
      dataUpdatedAt: result.dataUpdatedAt || undefined,
      isLoading: result.isLoading,
      isError: result.isError,
    }));
  }

  return namespaceIds.map((id) => ({
    namespaceId: id,
    data: batchQuery.data?.[id],
    dataUpdatedAt: batchQuery.dataUpdatedAt || undefined,
    isLoading: batchQuery.isLoading,
    isError: batchQuery.isError,
  }));
}

/**
 * Aggregate queue/topic/subscription stats for multiple namespaces, plus each namespace's
 * queue names (`.queues`, name-only — no per-queue detail is available from this hook; use
 * `useQueues(namespaceId)` for that) for entity-picker use cases like RulesPage's scope
 * validation.
 *
 * Fleet-scale finding: this used to run its own `N`-namespace `useQueries` fan-out to
 * `GET .../queues` *in addition to* `useNamespaceStats`'s own `N`-namespace fan-out to
 * `GET .../stats` — 2N live provider round-trips for what a single namespace stats fetch
 * already computes internally (it calls the same provider APIs to derive its totals). Now
 * backed entirely by `useNamespaceStats`'s one batched request; see its docs for the full
 * fan-out story. This also removes the stale "loading" window where the two independent
 * fetches settled at different times — the P1 misleading-zero bug class this fleet was
 * already bitten by once.
 */
export function useAllNamespacesQueues(
  namespaceIds: string[],
  autoRefresh: boolean = true,
  intervals?: { statsMs?: number },
): NamespaceQueueStats[] {
  const statsResults = useNamespaceStats(namespaceIds, autoRefresh, intervals?.statsMs ?? 60_000);

  return statsResults.map((result) => {
    const stats = result.data;
    const queues: Queue[] | undefined = stats?.queueNames.map((name) => ({
      name,
      activeMessageCount: 0,
      deadLetterMessageCount: 0,
      scheduledMessageCount: 0,
      maxSizeInMegabytes: 0,
      sizeInBytes: 0,
      status: 'Active',
    }));
    return {
      namespaceId: result.namespaceId,
      queues,
      topicNames: stats?.topicNames,
      totalActive: stats?.totalActive ?? 0,
      totalDlq: stats?.totalDlq ?? 0,
      totalScheduled: stats?.totalScheduled ?? 0,
      totalQueues: stats?.totalQueues ?? 0,
      totalTopics: stats?.totalTopics ?? 0,
      totalSubscriptions: stats?.totalSubscriptions ?? 0,
      dataUpdatedAt: result.dataUpdatedAt,
      isLoading: result.isLoading,
      isError: result.isError,
    };
  });
}
