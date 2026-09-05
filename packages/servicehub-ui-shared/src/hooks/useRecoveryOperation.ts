import { useQuery, useMutation, useQueryClient, UseQueryOptions } from '@tanstack/react-query';
import { recoveryApi, type RecoveryOperationDetail, type RecoveryRehearsal } from '../lib/api/recovery';
import { useDemoContext, rejectDemoModeMutation } from '../lib/demo/DemoContext';
import { getMockRecoveryOperationDetail, buildDemoRecoveryExportBundle } from '../lib/demo/mockProviders';
import toast from 'react-hot-toast';

function downloadDemoExport(operationId: string): void {
  const { filename, content, mimeType } = buildDemoRecoveryExportBundle(operationId);
  const blob = new Blob([content], { type: mimeType });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = filename;
  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);
  setTimeout(() => URL.revokeObjectURL(url), 0);
}

/**
 * Hook for fetching one recovery operation's full detail — header, entries, and event chain —
 * for the operation detail page.
 */
export function useRecoveryOperation(operationId: string | null) {
  const { isDemoMode, cloudProvider } = useDemoContext();

  const options: UseQueryOptions<RecoveryOperationDetail> =
    isDemoMode && cloudProvider
      ? {
          queryKey: ['recovery-operation', 'demo', cloudProvider, operationId],
          queryFn: (): Promise<RecoveryOperationDetail> => Promise.resolve(getMockRecoveryOperationDetail(operationId ?? undefined)),
          enabled: operationId !== null,
        }
      : {
          queryKey: ['recovery-operation', operationId],
          queryFn: () => recoveryApi.getOperationById(operationId!),
          enabled: !isDemoMode && operationId !== null,
          staleTime: 15_000,
        };

  return useQuery(options);
}

/**
 * Hook for writing off a non-terminal recovery ledger entry — the one operator declaration that
 * asserts a terminal outcome without ServiceHub having observed it. Requires a mandatory reason.
 */
export function useWriteOffRecoveryEntry() {
  const { isDemoMode } = useDemoContext();
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ entryId, reason }: { entryId: string; reason: string }) =>
      isDemoMode ? rejectDemoModeMutation() : recoveryApi.writeOff(entryId, reason),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['recovery-operation'] });
      queryClient.invalidateQueries({ queryKey: ['recovery-entries'] });
      queryClient.invalidateQueries({ queryKey: ['recovery-ageing'] });
      toast.success('Entry written off');
    },
    onError: (error: unknown) => {
      const err = error as { response?: { data?: { detail?: string; message?: string } }; message?: string };
      const msg = err?.response?.data?.detail || err?.response?.data?.message || err?.message || 'Failed to write off entry';
      toast.error(msg);
    },
  });
}

/**
 * Hook for rehearsing the Eligibility Gate against one ledger entry (roadmap W1.2) — "what would
 * the gate decide about this right now, and why."
 *
 * A mutation rather than a query only because the endpoint is a POST: nothing is written by it,
 * and the service behind it is architecturally incapable of reaching a broker. Deliberately
 * *not* blocked in Demo Mode's usual way — Demo Mode makes no backend calls at all, so it returns
 * a clearly-labelled decline instead of a fabricated verdict, since inventing a gate decision is
 * exactly the kind of fake evidence the whole product exists not to produce.
 */
export function useRehearseRecoveryEntry() {
  const { isDemoMode } = useDemoContext();

  return useMutation<RecoveryRehearsal, unknown, { entryId: string; actorKind?: 'Automation' | 'User' }>({
    mutationFn: ({ entryId, actorKind }) =>
      isDemoMode ? rejectDemoModeMutation() : recoveryApi.rehearse(entryId, actorKind),
    onError: (error: unknown) => {
      const err = error as { response?: { data?: { detail?: string; message?: string } }; message?: string };
      const msg = err?.response?.data?.detail || err?.response?.data?.message || err?.message || 'Rehearsal failed';
      toast.error(msg);
    },
  });
}

/**
 * Hook for downloading a recovery operation's evidence export. In Demo Mode this builds a
 * watermarked bundle entirely client-side from fixture data (see
 * `buildDemoRecoveryExportBundle`) rather than calling the real export endpoint — Demo Mode makes
 * no backend calls, and the download must still be unmistakably fixture data if the user saves it.
 */
export function useDownloadRecoveryExport() {
  const { isDemoMode } = useDemoContext();

  return useMutation({
    mutationFn: async ({ operationId, format }: { operationId: string; format: 'json' | 'csv' | 'package' }) => {
      if (isDemoMode) {
        downloadDemoExport(operationId);
        return;
      }
      await recoveryApi.downloadExport(operationId, format);
    },
    onSuccess: () => toast.success(isDemoMode ? 'Demo evidence export downloaded' : 'Evidence export downloaded'),
    onError: () => toast.error('Export failed. Please try again.'),
  });
}
