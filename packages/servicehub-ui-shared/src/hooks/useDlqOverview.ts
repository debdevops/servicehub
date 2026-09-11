import { useQuery, UseQueryOptions } from '@tanstack/react-query';
import { dlqOverviewApi, type DlqOverview, type DlqOverviewParams } from '../lib/api/dlqOverview';
import { useDemoContext } from '../lib/demo/DemoContext';
import { getMockDlqOverview } from '../lib/demo/mockProviders';

/**
 * Hook for the cross-cloud, provider-grouped DLQ overview — the triage dashboard answering
 * "which cloud, which reasons, which namespaces, and is it getting better or worse?"
 */
export function useDlqOverview(params: DlqOverviewParams = {}, enabled = true) {
  const { isDemoMode, cloudProvider } = useDemoContext();
  const { days = 7, cloud, environment, namespaceId, reason } = params;

  const options: UseQueryOptions<DlqOverview> =
    isDemoMode && cloudProvider
      ? {
          queryKey: ['dlq-overview', 'demo', cloudProvider],
          queryFn: (): Promise<DlqOverview> => Promise.resolve(getMockDlqOverview(cloudProvider)),
        }
      : {
          queryKey: ['dlq-overview', days, cloud, environment, namespaceId, reason],
          queryFn: () => dlqOverviewApi.getOverview(params),
          enabled: !isDemoMode && enabled,
          staleTime: 15_000,
          refetchInterval: 30_000,
          refetchIntervalInBackground: false,
          retry: (failureCount, error: unknown) => {
            const err = error as { response?: { status?: number } };
            if (err?.response?.status === 429) return false;
            return failureCount < 2;
          },
        };

  return useQuery(options);
}
