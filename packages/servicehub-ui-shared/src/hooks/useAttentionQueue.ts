import { useQuery, UseQueryOptions, UseQueryResult } from '@tanstack/react-query';
import { attentionQueueApi, type AttentionQueueResponse } from '@servicehub/ui-shared/lib/api/attentionQueue';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { getMockAttentionQueue } from '@servicehub/ui-shared/lib/demo/mockProviders';

export type { AttentionQueueItem, AttentionQueueResponse } from '@servicehub/ui-shared/lib/api/attentionQueue';

/** Home as a ranked attention queue (roadmap W2.2). */
export function useAttentionQueue(): UseQueryResult<AttentionQueueResponse, Error> {
  const { isDemoMode, cloudProvider } = useDemoContext();

  const options: UseQueryOptions<AttentionQueueResponse, Error> =
    isDemoMode && cloudProvider
      ? {
          queryKey: ['attention-queue', 'demo', cloudProvider],
          queryFn: (): Promise<AttentionQueueResponse> => Promise.resolve(getMockAttentionQueue(cloudProvider)),
        }
      : {
          queryKey: ['attention-queue'],
          queryFn: () => attentionQueueApi.get(),
          enabled: !isDemoMode,
          retry: 3,
          retryDelay: (attemptIndex) => Math.min(1000 * 2 ** attemptIndex, 30000),
          refetchInterval: 60000,
        };

  return useQuery(options);
}
