import { useEffect, useRef } from 'react';
import { useMutation, useQuery, useQueryClient, UseQueryOptions } from '@tanstack/react-query';
import { signatureReplayApi } from '../lib/api/signatureReplay';
import type { SignatureReplayFilter } from '../lib/api/signatureReplay';
import { isTerminalBulkOperationStatus } from '../lib/api/bulkOperations';
import type { PaginatedBulkOperationJobs } from '../lib/api/bulkOperations';
import type { ApiError } from '../lib/api/types';
import { useDemoContext, rejectDemoModeMutation } from '../lib/demo/DemoContext';
import { getMockSignatureReplayHistory } from '../lib/demo/mockProviders';
import toast from 'react-hot-toast';

const POLL_INTERVAL_MS = 1_500;

/** Dry-runs a signature replay — no intent headers, no mutation on the backend. */
export function useSignatureReplayPreview() {
  const { isDemoMode } = useDemoContext();
  return useMutation({
    mutationFn: ({
      namespaceId,
      signatureHash,
      filter,
    }: {
      namespaceId: string;
      signatureHash: string;
      filter: SignatureReplayFilter;
    }) =>
      isDemoMode
        ? rejectDemoModeMutation()
        : signatureReplayApi.preview(namespaceId, signatureHash, filter),
    onError: (error: ApiError) => {
      const message = error?.response?.data?.detail || error?.message || 'Failed to preview the signature replay';
      toast.error(message, { duration: 6000 });
    },
  });
}

/** Starts a replay of a failure signature's member messages. Returns the job in Pending status. */
export function useStartSignatureReplay() {
  const { isDemoMode } = useDemoContext();

  return useMutation({
    mutationFn: ({
      namespaceId,
      signatureHash,
      filter,
    }: {
      namespaceId: string;
      signatureHash: string;
      filter: SignatureReplayFilter;
    }) =>
      isDemoMode
        ? rejectDemoModeMutation()
        : signatureReplayApi.start(namespaceId, signatureHash, filter),
    onSuccess: (job) => {
      toast.success(`Signature replay started — ${job.totalMatched} message(s) queued`);
    },
    onError: (error: ApiError) => {
      const message = error?.response?.data?.detail || error?.message || 'Failed to start the signature replay';
      toast.error(message, { duration: 8000 });
    },
  });
}

/**
 * Polls a single signature-replay job's progress until it reaches a terminal status. Mirrors
 * useBulkOperationJob — same poll interval, same terminal-status toasts — invalidating the
 * signature detail (instead of the bulk-operations list) on completion so the Signature Details
 * page's related-messages/status refresh.
 */
export function useSignatureReplayJob(
  jobId: string | null,
  namespaceId?: string,
  signatureHash?: string,
) {
  const queryClient = useQueryClient();
  const notifiedRef = useRef<string | null>(null);
  const { isDemoMode } = useDemoContext();

  const query = useQuery({
    queryKey: ['signature-replay', 'job', jobId],
    queryFn: () => signatureReplayApi.getJob(jobId!),
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
    void queryClient.invalidateQueries({ queryKey: ['dlq-signature-detail', namespaceId, signatureHash] });
    void queryClient.invalidateQueries({ queryKey: ['fleet-overview'] });

    if (job.status === 'Completed') {
      toast.success(`Signature replay completed — ${job.successCount}/${job.totalMatched} succeeded`);
    } else if (job.status === 'CompletedWithErrors') {
      toast.error(
        `Signature replay finished with issues — ${job.successCount} succeeded, ${job.failureCount} failed, ${job.skippedCount} skipped`,
        { duration: 8000 },
      );
    } else if (job.status === 'Cancelled') {
      toast(`Signature replay cancelled after ${job.processedCount}/${job.totalMatched} message(s)`, { icon: '⏹️' });
    } else if (job.status === 'Failed') {
      toast.error(job.errorSummary || 'Signature replay failed', { duration: 8000 });
    }
  }, [query.data, queryClient, namespaceId, signatureHash]);

  return query;
}

/**
 * Lists past and in-flight replay jobs for one failure signature, most recent first — the
 * Replay Safety & History panel's data source. Wraps the already-existing, already-deployed
 * replay-history endpoint (previously unused by the frontend).
 */
export function useSignatureReplayHistory(
  namespaceId?: string,
  signatureHash?: string,
  page = 1,
  pageSize = 20,
) {
  const { isDemoMode, cloudProvider } = useDemoContext();

  const options: UseQueryOptions<PaginatedBulkOperationJobs, unknown> = isDemoMode && cloudProvider
    ? {
        queryKey: ['signature-replay-history', 'demo', cloudProvider, signatureHash],
        queryFn: (): Promise<PaginatedBulkOperationJobs> => {
          const history = getMockSignatureReplayHistory(signatureHash!);
          if (!history) return Promise.reject(new Error('Signature not found'));
          return Promise.resolve(history);
        },
        enabled: !!signatureHash,
        staleTime: Infinity,
        retry: false,
      }
    : {
        queryKey: ['signature-replay-history', namespaceId, signatureHash, page, pageSize],
        queryFn: () => signatureReplayApi.history(namespaceId!, signatureHash!, page, pageSize),
        enabled: !!namespaceId && !!signatureHash,
        staleTime: 30_000,
      };

  return useQuery(options);
}

/** Requests cancellation of a pending/running signature-replay job. */
export function useCancelSignatureReplayJob() {
  const queryClient = useQueryClient();
  const { isDemoMode } = useDemoContext();

  return useMutation({
    mutationFn: (jobId: string) => (isDemoMode ? rejectDemoModeMutation() : signatureReplayApi.cancelJob(jobId)),
    onSuccess: (job) => {
      queryClient.setQueryData(['signature-replay', 'job', job.id], job);
    },
    onError: (error: ApiError) => {
      const message = error?.response?.data?.detail || error?.message || 'Failed to cancel the signature replay';
      toast.error(message);
    },
  });
}
