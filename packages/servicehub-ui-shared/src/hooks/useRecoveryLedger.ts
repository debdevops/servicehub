import { useQuery, UseQueryOptions } from '@tanstack/react-query';
import {
  recoveryApi,
  type RecoveryOperation,
  type RecoveryLedgerEntry,
  type RecoveryEntriesParams,
  type SignatureAutonomyStatus,
  type ApprovalQueueEntry,
} from '../lib/api/recovery';
import { useDemoContext } from '../lib/demo/DemoContext';
import { getMockRecoveryOperations, getMockRecoveryOperationDetail } from '../lib/demo/mockProviders';

/**
 * Hook for fetching recovery operations, most recently opened first. The Recovery Ledger page's
 * primary list.
 */
export function useRecoveryOperations(namespaceId?: string, enabled = true) {
  const { isDemoMode, cloudProvider } = useDemoContext();

  const options: UseQueryOptions<RecoveryOperation[]> =
    isDemoMode && cloudProvider
      ? {
          queryKey: ['recovery-operations', 'demo', cloudProvider],
          queryFn: (): Promise<RecoveryOperation[]> => Promise.resolve(getMockRecoveryOperations()),
        }
      : {
          queryKey: ['recovery-operations', namespaceId],
          queryFn: () => recoveryApi.getOperations(namespaceId),
          enabled: !isDemoMode && enabled,
          staleTime: 30_000,
          refetchInterval: 60_000,
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

/**
 * Hook for fetching a failure signature's actual current autonomy status — the same read the
 * backend's Eligibility Gate predicate 5 performs, so the UI can show an operator exactly why
 * automatic replay is or isn't available, using only real facts. Demo mode has no live
 * AutonomyGrant to read, so it derives the same real, product-documented capability facts
 * (Azure can prove DLQ absence; AWS/GCP cannot) from the fixture's own cloud provider, at the
 * L3 floor every signature starts at — never a fabricated grant or reason.
 */
export function useSignatureAutonomyStatus(signatureHash?: string, actionKind: 'Replay' | 'Purge' = 'Replay') {
  const { isDemoMode, cloudProvider } = useDemoContext();

  const options: UseQueryOptions<SignatureAutonomyStatus> =
    isDemoMode && cloudProvider
      ? {
          queryKey: ['recovery-autonomy-status', 'demo', cloudProvider, signatureHash],
          queryFn: (): Promise<SignatureAutonomyStatus> => {
            const canProveDlqAbsence = cloudProvider === 'azure';
            return Promise.resolve({
              signatureHash: signatureHash ?? '',
              actionKind,
              currentLevel: 3,
              levelLabel: 'Approve (L3)',
              canAutoReplay: false,
              canProveDlqAbsence,
              blockedReason: canProveDlqAbsence
                ? 'This signature has not yet earned Standing (L4) or Unattended (L5) trust — replay currently requires human approval (L3).'
                : `${cloudProvider.toUpperCase()} cannot currently provide the deterministic recovery evidence required for unattended replay — background scanning cannot independently confirm a replayed message never returned to the DLQ.`,
            });
          },
          enabled: !!signatureHash,
          staleTime: Infinity,
          retry: false,
        }
      : {
          queryKey: ['recovery-autonomy-status', signatureHash, actionKind],
          queryFn: () => recoveryApi.getAutonomyStatus(signatureHash!),
          enabled: !isDemoMode && !!signatureHash,
          staleTime: 30_000,
        };

  return useQuery(options);
}

/**
 * Hook for fetching the Approval Queue (roadmap §11 item 1): auto-replay rule matches the
 * Eligibility Gate escalated for manual review, still `Active` and so still approvable. Demo mode
 * has no synthetic Declined ledger fixture — rather than fabricate one, it honestly reports an
 * empty queue.
 */
export function useApprovalQueue(namespaceId?: string, limit = 100) {
  const { isDemoMode } = useDemoContext();

  const options: UseQueryOptions<ApprovalQueueEntry[]> = isDemoMode
    ? {
        queryKey: ['recovery-approval-queue', 'demo'],
        queryFn: (): Promise<ApprovalQueueEntry[]> => Promise.resolve([]),
      }
    : {
        queryKey: ['recovery-approval-queue', namespaceId, limit],
        queryFn: () => recoveryApi.getApprovalQueue(namespaceId, limit),
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

/**
 * Hook for fetching recovery ledger entries, optionally filtered by operation, namespace, or
 * source DLQ message. The `dlqMessageId` filter powers the message-detail "recovery record"
 * integration (roadmap §13.2).
 */
export function useRecoveryEntries(params: RecoveryEntriesParams, enabled = true) {
  const { isDemoMode, cloudProvider } = useDemoContext();

  const options: UseQueryOptions<RecoveryLedgerEntry[]> =
    isDemoMode && cloudProvider
      ? {
          queryKey: ['recovery-entries', 'demo', cloudProvider, params],
          queryFn: (): Promise<RecoveryLedgerEntry[]> => {
            const { entries } = getMockRecoveryOperationDetail(params.operationId);
            if (params.dlqMessageId != null) {
              return Promise.resolve(entries.filter(e => e.dlqMessageId === params.dlqMessageId));
            }
            return Promise.resolve(entries);
          },
        }
      : {
          queryKey: ['recovery-entries', params],
          queryFn: () => recoveryApi.getEntries(params),
          enabled: !isDemoMode && enabled,
          staleTime: 30_000,
          retry: (failureCount, error: unknown) => {
            const err = error as { response?: { status?: number } };
            if (err?.response?.status === 404) return false;
            if (err?.response?.status === 403) return false;
            return failureCount < 2;
          },
        };

  return useQuery(options);
}
