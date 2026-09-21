import { useQuery, UseQueryOptions } from '@tanstack/react-query';
import { recoveryApi, type RecoveryLedgerEntry } from '../lib/api/recovery';
import { useDemoContext } from '../lib/demo/DemoContext';
import { getMockRecoveryAgeing } from '../lib/demo/mockProviders';

/**
 * Hook for fetching the caller's currently open (non-terminal) recovery ledger entries — the
 * falsifiable form of "nothing is silently lost" (roadmap §7.2). Every entry past the ageing
 * threshold still appears here, unconditionally, until it resolves or the ageing worker expires
 * it.
 */
export function useRecoveryAgeing(enabled = true) {
  const { isDemoMode, cloudProvider } = useDemoContext();

  const options: UseQueryOptions<RecoveryLedgerEntry[]> =
    isDemoMode && cloudProvider
      ? {
          queryKey: ['recovery-ageing', 'demo', cloudProvider],
          queryFn: (): Promise<RecoveryLedgerEntry[]> => Promise.resolve(getMockRecoveryAgeing()),
        }
      : {
          queryKey: ['recovery-ageing'],
          queryFn: () => recoveryApi.getAgeing(),
          enabled: !isDemoMode && enabled,
          staleTime: 30_000,
          refetchInterval: 60_000,
          refetchIntervalInBackground: false,
        };

  return useQuery(options);
}
