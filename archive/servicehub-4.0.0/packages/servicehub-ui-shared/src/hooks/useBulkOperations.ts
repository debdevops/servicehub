import { useEffect, useRef } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  bulkOperationsApi,
  isTerminalBulkOperationStatus,
  type BulkOperationFilter,
  type BulkOperationType,
} from '../lib/api/bulkOperations';
import type { ApiError } from '../lib/api/types';
import { useDemoContext, rejectDemoModeMutation } from '../lib/demo/DemoContext';
import toast from 'react-hot-toast';

const POLL_INTERVAL_MS = 1_500;

/** Dry-runs a bulk replay/purge — no intent headers, no mutation on the backend. */
export function useBulkOperationPreview() {
  const { isDemoMode } = useDemoContext();
  return useMutation({
    mutationFn: ({ operationType, filter }: { operationType: BulkOperationType; filter: BulkOperationFilter }) =>
      isDemoMode ? rejectDemoModeMutation() : bulkOperationsApi.preview(operationType, filter),
    onError: (error: ApiError) => {
      const message = error?.response?.data?.detail || error?.message || 'Failed to preview the bulk operation';
      toast.error(message, { duration: 6000 });
    },
  });
}

/** Creates and enqueues a bulk replay/purge job. Returns the job in Pending status. */
export function useCreateBulkOperation() {
  const queryClient = useQueryClient();
  const { isDemoMode } = useDemoContext();

  return useMutation({
    mutationFn: ({ operationType, filter }: { operationType: BulkOperationType; filter: BulkOperationFilter }) =>
      isDemoMode ? rejectDemoModeMutation() : bulkOperationsApi.create(operationType, filter),
    onSuccess: (job) => {
      void queryClient.invalidateQueries({ queryKey: ['bulk-operations', 'list'] });
      toast.success(`Bulk ${job.operationType.toLowerCase()} started — ${job.totalMatched} message(s) queued`);
    },
    onError: (error: ApiError) => {
      const message = error?.response?.data?.detail || error?.message || 'Failed to start the bulk operation';
      toast.error(message, { duration: 8000 });
    },
  });
}

/**
 * Polls a single job's progress until it reaches a terminal status. Polling (not SSE) is the
 * primary progress channel by design — see docs/FLOW.md's note on the platform event bus
 * stripping payloads from the SSE wire format, making it unsuited to precise progress.
 */
export function useBulkOperationJob(jobId: string | null) {
  const queryClient = useQueryClient();
  const notifiedRef = useRef<string | null>(null);
  const { isDemoMode } = useDemoContext();

  const query = useQuery({
    queryKey: ['bulk-operations', 'job', jobId],
    queryFn: () => bulkOperationsApi.get(jobId!),
    enabled: !isDemoMode && !!jobId,
    refetchInterval: (q) => {
      const status = q.state.data?.status;
      return status && isTerminalBulkOperationStatus(status) ? false : POLL_INTERVAL_MS;
    },
  });

  useEffect(() => {
    const job = query.data;
    if (!job || !isTerminalBulkOperationStatus(job.status) || notifiedRef.current === job.id) return;
    notifiedRef.current = job.id;

    void queryClient.invalidateQueries({ queryKey: ['dlq-history'] });
    void queryClient.invalidateQueries({ queryKey: ['dlq-summary'] });
    void queryClient.invalidateQueries({ queryKey: ['bulk-operations', 'list'] });
    void queryClient.invalidateQueries({ queryKey: ['fleet-overview'] });
    // Recovery Ledger otherwise only refreshes on its own 60s refetchInterval — without this,
    // a bulk replay/purge that just recorded ledger entries can take up to a minute to show up
    // on the Recovery Ledger page.
    void queryClient.invalidateQueries({ queryKey: ['recovery-operations'] });
    void queryClient.invalidateQueries({ queryKey: ['recovery-entries'] });
    // Sidebar/queue-list DLQ counts and the Failure Signatures list otherwise never refresh
    // after a bulk job completes — dlq-signatures in particular has no refetchInterval at all.
    void queryClient.invalidateQueries({ queryKey: ['queues', job.namespaceId] });
    void queryClient.invalidateQueries({ queryKey: ['namespace-stats', job.namespaceId] });
    void queryClient.invalidateQueries({ queryKey: ['dlq-signatures', job.namespaceId] });
    void queryClient.invalidateQueries({ queryKey: ['dlq-signature-detail', job.namespaceId], exact: false });

    if (job.status === 'Completed') {
      toast.success(`Bulk ${job.operationType.toLowerCase()} completed — ${job.successCount}/${job.totalMatched} succeeded`);
    } else if (job.status === 'CompletedWithErrors') {
      toast.error(
        `Bulk ${job.operationType.toLowerCase()} finished with issues — ${job.successCount} succeeded, ${job.failureCount} failed, ${job.skippedCount} skipped`,
        { duration: 8000 },
      );
    } else if (job.status === 'Cancelled') {
      toast(`Bulk ${job.operationType.toLowerCase()} cancelled after ${job.processedCount}/${job.totalMatched} message(s)`, { icon: '⏹️' });
    } else if (job.status === 'Failed') {
      toast.error(job.errorSummary || `Bulk ${job.operationType.toLowerCase()} failed`, { duration: 8000 });
    }
  }, [query.data, queryClient]);

  return query;
}

/** Lists past bulk operation jobs for the owner, optionally scoped to a namespace. */
export function useBulkOperationJobs(namespaceId?: string) {
  const { isDemoMode } = useDemoContext();

  return useQuery({
    queryKey: ['bulk-operations', 'list', namespaceId],
    queryFn: () => bulkOperationsApi.list(namespaceId),
    enabled: !isDemoMode,
    staleTime: 10_000,
  });
}

/** Requests cancellation of a pending/running job. */
export function useCancelBulkOperation() {
  const queryClient = useQueryClient();
  const { isDemoMode } = useDemoContext();

  return useMutation({
    mutationFn: (jobId: string) => (isDemoMode ? rejectDemoModeMutation() : bulkOperationsApi.cancel(jobId)),
    onSuccess: (job) => {
      queryClient.setQueryData(['bulk-operations', 'job', job.id], job);
    },
    onError: (error: ApiError) => {
      const message = error?.response?.data?.detail || error?.message || 'Failed to cancel the bulk operation';
      toast.error(message);
    },
  });
}
