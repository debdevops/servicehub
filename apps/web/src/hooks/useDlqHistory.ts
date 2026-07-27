import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  dlqHistoryApi,
  type DlqHistoryParams,
  type DlqTriageStatus,
} from '@/lib/api/dlqHistory';
import { useDemoContext, rejectDemoModeMutation } from '@/lib/demo/DemoContext';
import toast from 'react-hot-toast';

/**
 * Hook for fetching paginated DLQ history.
 */
export function useDlqHistory(params: DlqHistoryParams, enabled = true) {
  const { isDemoMode } = useDemoContext();

  return useQuery({
    queryKey: ['dlq-history', params],
    queryFn: () => dlqHistoryApi.getHistory(params),
    enabled: !isDemoMode && enabled,
    staleTime: 15_000,
    refetchInterval: 30_000,
    refetchIntervalInBackground: false,
    retry: (failureCount, error: unknown) => {
      const err = error as { response?: { status?: number } };
      if (err?.response?.status === 404) return false;
      if (err?.response?.status === 429) return false;
      return failureCount < 2;
    },
  });
}

/**
 * Hook for fetching a single DLQ message detail.
 */
export function useDlqMessageDetail(id: number | null) {
  const { isDemoMode } = useDemoContext();

  return useQuery({
    queryKey: ['dlq-message', id],
    queryFn: () => dlqHistoryApi.getById(id!),
    enabled: !isDemoMode && id !== null,
    staleTime: 30_000,
  });
}

/**
 * Hook for fetching the timeline of a DLQ message.
 */
export function useDlqTimeline(id: number | null) {
  const { isDemoMode } = useDemoContext();

  return useQuery({
    queryKey: ['dlq-timeline', id],
    queryFn: () => dlqHistoryApi.getTimeline(id!),
    enabled: !isDemoMode && id !== null,
    staleTime: 30_000,
  });
}

/**
 * Hook for fetching DLQ summary statistics.
 */
export function useDlqSummary(namespaceId?: string) {
  const { isDemoMode } = useDemoContext();

  return useQuery({
    queryKey: ['dlq-summary', namespaceId],
    queryFn: () => dlqHistoryApi.getSummary(namespaceId),
    enabled: !isDemoMode && !!namespaceId,
    staleTime: 15_000,
    refetchInterval: 30_000,
    refetchIntervalInBackground: false,
    retry: (failureCount, error: unknown) => {
      const err = error as { response?: { status?: number } };
      if (err?.response?.status === 404) return false;
      if (err?.response?.status === 429) return false;
      return failureCount < 2;
    },
  });
}

/**
 * Hook for updating notes on a DLQ message.
 */
export function useUpdateDlqNotes() {
  const queryClient = useQueryClient();
  const { isDemoMode } = useDemoContext();

  return useMutation({
    mutationFn: ({ id, notes }: { id: number; notes: string }) =>
      isDemoMode ? rejectDemoModeMutation() : dlqHistoryApi.updateNotes(id, notes),
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: ['dlq-history'] });
      queryClient.invalidateQueries({ queryKey: ['dlq-message', variables.id] });
      toast.success('Notes updated successfully');
    },
    onError: (error: unknown) => {
      const err = error as { response?: { data?: { message?: string } }; message?: string };
      const msg = err?.response?.data?.message || err?.message || 'Failed to update notes';
      toast.error(msg);
    },
  });
}

/**
 * Hook for triaging a DLQ message (status transition). Turns the DLQ history into a
 * triage inbox: Resolve / Archive / Ignore (Discard) / Reopen.
 */
export function useUpdateDlqStatus() {
  const queryClient = useQueryClient();
  const { isDemoMode } = useDemoContext();

  return useMutation({
    mutationFn: ({ id, status, notes }: { id: number; status: DlqTriageStatus; notes?: string }) =>
      isDemoMode ? rejectDemoModeMutation() : dlqHistoryApi.updateStatus(id, status, notes),
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: ['dlq-history'] });
      queryClient.invalidateQueries({ queryKey: ['dlq-message', variables.id] });
      queryClient.invalidateQueries({ queryKey: ['dlq-summary'] });
      queryClient.invalidateQueries({ queryKey: ['fleet-overview'] });
      toast.success(`Message marked ${variables.status.toLowerCase()}`);
    },
    onError: (error: unknown) => {
      const err = error as { response?: { data?: { message?: string; detail?: string } }; message?: string };
      const msg =
        err?.response?.data?.detail ||
        err?.response?.data?.message ||
        err?.message ||
        'Failed to update status';
      toast.error(msg);
    },
  });
}
