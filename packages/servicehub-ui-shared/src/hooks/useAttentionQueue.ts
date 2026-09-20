import { useQuery, UseQueryOptions, UseQueryResult } from '@tanstack/react-query';
import { attentionQueueApi, type AttentionQueueResponse } from '@servicehub/ui-shared/lib/api/attentionQueue';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { getMockAttentionQueue } from '@servicehub/ui-shared/lib/demo/mockProviders';
import type { CloudProviderType } from '@servicehub/ui-shared/lib/api/types';

export type { AttentionQueueItem, AttentionQueueResponse } from '@servicehub/ui-shared/lib/api/attentionQueue';

/**
 * Home as a ranked attention queue (roadmap W2.2). `provider`, when passed in the real (non-demo)
 * app, scopes the queue to a cloud-specific Home — ignored in Demo Mode, where the route's own
 * `cloudProvider` already fixes it.
 */
export function useAttentionQueue(provider?: CloudProviderType): UseQueryResult<AttentionQueueResponse, Error> {
  const { isDemoMode, cloudProvider } = useDemoContext();

  const options: UseQueryOptions<AttentionQueueResponse, Error> =
    isDemoMode && cloudProvider
      ? {
          queryKey: ['attention-queue', 'demo', cloudProvider],
          queryFn: (): Promise<AttentionQueueResponse> => Promise.resolve(getMockAttentionQueue(cloudProvider)),
        }
      : {
          queryKey: ['attention-queue', provider ?? 'all'],
          queryFn: () => attentionQueueApi.get(provider),
          enabled: !isDemoMode,
          retry: 3,
          retryDelay: (attemptIndex) => Math.min(1000 * 2 ** attemptIndex, 30000),
          refetchInterval: 60000,
        };

  return useQuery(options);
}
