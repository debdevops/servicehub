import { useQuery, useMutation, useQueryClient, UseQueryOptions } from '@tanstack/react-query';
import toast from 'react-hot-toast';
import {
  playbookApi,
  type PlaybookEntry,
  type PlaybookEntryDetail,
  type PlaybookEntriesParams,
  type PlaybookDisposition,
  type CorrelationAccountabilityReport,
  type BacktestReport,
} from '../lib/api/playbook';
import { useDemoContext, rejectDemoModeMutation } from '../lib/demo/DemoContext';

/**
 * Hook for fetching Playbook Ledger entries, most recently proposed first, optionally filtered by
 * pillar, namespace, or lifecycle state. Demo Mode has no synthetic proposal/disposition fixture —
 * rather than fabricate one, it honestly reports an empty list, same reasoning as
 * `useApprovalQueue`/`useAutonomyDashboard`.
 */
export function usePlaybookEntries(params: PlaybookEntriesParams = {}) {
  const { isDemoMode } = useDemoContext();

  const options: UseQueryOptions<PlaybookEntry[]> = isDemoMode
    ? {
        queryKey: ['playbook-entries', 'demo', params],
        queryFn: (): Promise<PlaybookEntry[]> => Promise.resolve([]),
      }
    : {
        queryKey: ['playbook-entries', params],
        queryFn: () => playbookApi.getEntries(params),
        enabled: !isDemoMode,
        staleTime: 15_000,
        refetchInterval: 30_000,
        refetchIntervalInBackground: false,
        retry: (failureCount, error: unknown) => {
          const err = error as { response?: { status?: number } };
          if (err?.response?.status === 404) return false;
          if (err?.response?.status === 403) return false;
          return failureCount < 2;
        },
      };

  return useQuery(options);
}

/** Hook for fetching one Playbook Ledger entry's current projection plus its full event chain. */
export function usePlaybookEntry(entryId: string | null) {
  const { isDemoMode } = useDemoContext();

  const options: UseQueryOptions<PlaybookEntryDetail> = {
    queryKey: ['playbook-entry', entryId],
    queryFn: () => playbookApi.getEntryById(entryId!),
    enabled: !isDemoMode && entryId !== null,
    staleTime: 15_000,
  };

  return useQuery(options);
}

/** Hook for marking a Playbook Ledger entry under review — a UX nicety, valid only from `Proposed`. */
export function useMarkPlaybookEntryUnderReview() {
  const { isDemoMode } = useDemoContext();
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (entryId: string) => (isDemoMode ? rejectDemoModeMutation() : playbookApi.markUnderReview(entryId)),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['playbook-entries'] });
      queryClient.invalidateQueries({ queryKey: ['playbook-entry'] });
    },
    onError: (error: unknown) => {
      const err = error as { response?: { data?: { detail?: string; message?: string } }; message?: string };
      const msg = err?.response?.data?.detail || err?.response?.data?.message || err?.message || 'Failed to mark entry under review';
      toast.error(msg);
    },
  });
}

/**
 * Hook for recording a human's terminal decision on a proposal — approve or reject. This is the
 * one human-in-the-loop gate the Playbook Ledger has: approving here means "a human agrees this is
 * sound," never itself triggering a replay or purge.
 */
export function useDispositionPlaybookEntry() {
  const { isDemoMode } = useDemoContext();
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ entryId, disposition, reason }: { entryId: string; disposition: PlaybookDisposition; reason?: string }) =>
      isDemoMode ? rejectDemoModeMutation() : playbookApi.disposition(entryId, disposition, reason),
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: ['playbook-entries'] });
      queryClient.invalidateQueries({ queryKey: ['playbook-entry'] });
      queryClient.invalidateQueries({ queryKey: ['playbook-correlation-accountability'] });
      queryClient.invalidateQueries({ queryKey: ['playbook-backtest'] });
      toast.success(variables.disposition === 'Approved' ? 'Proposal approved' : 'Proposal rejected');
    },
    onError: (error: unknown) => {
      const err = error as { response?: { data?: { detail?: string; message?: string } }; message?: string };
      const msg = err?.response?.data?.detail || err?.response?.data?.message || err?.message || 'Failed to record disposition';
      toast.error(msg);
    },
  });
}

/**
 * Hook for fetching the correlation accountability report (roadmap §5.D C4, §11 item 17) — how
 * many correlation hypotheses ServiceHub has proposed and what humans decided about them. Demo
 * Mode has no synthetic disposition history — rather than fabricate one, it honestly reports an
 * all-zero, no-data snapshot, same reasoning as `usePlaybookEntries`.
 */
export function useCorrelationAccountability() {
  const { isDemoMode } = useDemoContext();

  const options: UseQueryOptions<CorrelationAccountabilityReport> = isDemoMode
    ? {
        queryKey: ['playbook-correlation-accountability', 'demo'],
        queryFn: (): Promise<CorrelationAccountabilityReport> =>
          Promise.resolve({
            generatedAt: new Date().toISOString(),
            totalHypotheses: 0,
            proposedCount: 0,
            underReviewCount: 0,
            approvedCount: 0,
            rejectedCount: 0,
            expiredCount: 0,
            supersededCount: 0,
            approvalRate: null,
          }),
      }
    : {
        queryKey: ['playbook-correlation-accountability'],
        queryFn: () => playbookApi.getCorrelationAccountability(),
        enabled: !isDemoMode,
        staleTime: 30_000,
      };

  return useQuery(options);
}

/**
 * Hook for fetching the counterfactual backtest report (roadmap §11 item 14) — whether
 * dispositioned anomaly-flag (I3) and drift-finding (P2) proposals were followed by real recovery
 * activity for the same entity. Demo Mode has no synthetic disposition/recovery history — rather
 * than fabricate one, it honestly reports an all-zero, no-data snapshot, same reasoning as
 * `useCorrelationAccountability`.
 */
export function useBacktestReport() {
  const { isDemoMode } = useDemoContext();

  const options: UseQueryOptions<BacktestReport> = isDemoMode
    ? {
        queryKey: ['playbook-backtest', 'demo'],
        queryFn: (): Promise<BacktestReport> =>
          Promise.resolve({
            generatedAt: new Date().toISOString(),
            totalBacktested: 0,
            corroboratedCount: 0,
            corroborationRate: null,
            entries: [],
          }),
      }
    : {
        queryKey: ['playbook-backtest'],
        queryFn: () => playbookApi.getBacktest(),
        enabled: !isDemoMode,
        staleTime: 30_000,
      };

  return useQuery(options);
}
