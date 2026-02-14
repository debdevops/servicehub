import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  dlqHistoryApi,
  type DlqHistoryParams,
} from '@/lib/api/dlqHistory';
import toast from 'react-hot-toast';

/**
 * Hook for fetching paginated DLQ history.
 */
export function useDlqHistory(params: DlqHistoryParams, enabled = true) {
  return useQuery({
    queryKey: ['dlq-history', params],
    queryFn: () => dlqHistoryApi.getHistory(params),
    enabled,
    staleTime: 10_000,
    refetchInterval: 30_000,
    retry: (failureCount, error: unknown) => {
      const err = error as { response?: { status?: number } };
      if (err?.response?.status === 404) return false;
      return failureCount < 2;
    },
  });
}

/**
 * Hook for fetching a single DLQ message detail.
 */
export function useDlqMessageDetail(id: number | null) {
  return useQuery({
    queryKey: ['dlq-message', id],
    queryFn: () => dlqHistoryApi.getById(id!),
    enabled: id !== null,
    staleTime: 30_000,
  });
}

/**
 * Hook for fetching the timeline of a DLQ message.
 */
export function useDlqTimeline(id: number | null) {
  return useQuery({
    queryKey: ['dlq-timeline', id],
    queryFn: () => dlqHistoryApi.getTimeline(id!),
    enabled: id !== null,
    staleTime: 30_000,
  });
}

/**
 * Hook for fetching DLQ summary statistics.
 */
export function useDlqSummary(namespaceId?: string) {
  return useQuery({
    queryKey: ['dlq-summary', namespaceId],
    queryFn: () => dlqHistoryApi.getSummary(namespaceId),
    staleTime: 30_000,
    refetchInterval: 60_000,
    retry: (failureCount, error: unknown) => {
      const err = error as { response?: { status?: number } };
      if (err?.response?.status === 404) return false;
      return failureCount < 2;
    },
  });
}

/**
 * Hook for updating notes on a DLQ message.
 */
export function useUpdateDlqNotes() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, notes }: { id: number; notes: string }) =>
      dlqHistoryApi.updateNotes(id, notes),
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
