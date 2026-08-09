import { useQuery, useMutation, useQueryClient, UseQueryOptions } from '@tanstack/react-query';
import {
  dlqSignaturesApi,
  DlqSignaturesResponse,
  DlqSignatureDetail,
  SignatureTimelineResponse,
  RootCauseExplorerResponse,
  UpsertKnowledgeRequest,
} from '../lib/api/dlqSignatures';
import { useDemoContext, rejectDemoModeMutation } from '../lib/demo/DemoContext';
import {
  getMockDlqSignatures,
  getMockDlqSignatureDetail,
  getMockSignatureTimeline,
  getMockRootCauseMatches,
} from '../lib/demo/mockProviders';
import toast from 'react-hot-toast';

/**
 * Hook for fetching a namespace's DLQ error-cluster signatures. Treats the AI-unavailable
 * response (`available: false`) as a normal state, never as an error — the 200 always carries
 * a well-formed body.
 */
export function useDlqSignatures(namespaceId?: string) {
  const { isDemoMode, cloudProvider } = useDemoContext();

  const options: UseQueryOptions<DlqSignaturesResponse, unknown> = isDemoMode && cloudProvider
    ? {
        queryKey: ['dlq-signatures', 'demo', cloudProvider],
        queryFn: (): Promise<DlqSignaturesResponse> => Promise.resolve(getMockDlqSignatures(cloudProvider)),
        staleTime: Infinity,
        retry: false,
      }
    : {
        queryKey: ['dlq-signatures', namespaceId],
        queryFn: () => dlqSignaturesApi.getSignatures(namespaceId!),
        enabled: !!namespaceId,
        staleTime: 60_000,
        retry: (failureCount, error: unknown) => {
          const err = error as { response?: { status?: number } };
          if (err?.response?.status === 404) return false;
          if (err?.response?.status === 429) return false;
          return failureCount < 2;
        },
      };

  const query = useQuery(options);

  return {
    data: query.data,
    loading: query.isLoading,
    error: query.error,
    available: query.data?.available ?? false,
  };
}

/**
 * Hook for fetching full detail for a single failure signature.
 */
export function useDlqSignatureDetail(namespaceId?: string, signatureHash?: string) {
  const { isDemoMode, cloudProvider } = useDemoContext();

  const options: UseQueryOptions<DlqSignatureDetail, unknown> = isDemoMode && cloudProvider
    ? {
        queryKey: ['dlq-signature-detail', 'demo', cloudProvider, signatureHash],
        queryFn: (): Promise<DlqSignatureDetail> => {
          const detail = getMockDlqSignatureDetail(cloudProvider, signatureHash!);
          if (!detail) return Promise.reject(new Error('Signature not found'));
          return Promise.resolve(detail);
        },
        enabled: !!signatureHash,
        staleTime: Infinity,
        retry: false,
      }
    : {
        queryKey: ['dlq-signature-detail', namespaceId, signatureHash],
        queryFn: () => dlqSignaturesApi.getSignatureDetail(namespaceId!, signatureHash!),
        enabled: !!namespaceId && !!signatureHash,
        staleTime: 30_000,
      };

  return useQuery(options);
}

/**
 * Hook for fetching a failure signature's lifecycle timeline.
 */
export function useSignatureTimeline(namespaceId?: string, signatureHash?: string) {
  const { isDemoMode, cloudProvider } = useDemoContext();

  const options: UseQueryOptions<SignatureTimelineResponse, unknown> = isDemoMode && cloudProvider
    ? {
        queryKey: ['dlq-signature-timeline', 'demo', cloudProvider, signatureHash],
        queryFn: (): Promise<SignatureTimelineResponse> => {
          const timeline = getMockSignatureTimeline(signatureHash!);
          if (!timeline) return Promise.reject(new Error('Signature not found'));
          return Promise.resolve(timeline);
        },
        enabled: !!signatureHash,
        staleTime: Infinity,
        retry: false,
      }
    : {
        queryKey: ['dlq-signature-timeline', namespaceId, signatureHash],
        queryFn: () => dlqSignaturesApi.getSignatureTimeline(namespaceId!, signatureHash!),
        enabled: !!namespaceId && !!signatureHash,
        staleTime: 30_000,
      };

  return useQuery(options);
}

/**
 * Root Cause Explorer — fetches this signature's occurrences in every other namespace in the
 * fleet, plus the resulting total occurrence count across the whole fleet. Matching is exact
 * signature-hash equality only, computed server-side; this hook does no matching of its own.
 */
export function useRootCauseMatches(namespaceId?: string, signatureHash?: string) {
  const { isDemoMode, cloudProvider } = useDemoContext();

  const options: UseQueryOptions<RootCauseExplorerResponse, unknown> = isDemoMode && cloudProvider
    ? {
        queryKey: ['dlq-signature-root-cause-matches', 'demo', cloudProvider, signatureHash],
        queryFn: (): Promise<RootCauseExplorerResponse> => {
          const matches = getMockRootCauseMatches(signatureHash!);
          if (!matches) return Promise.reject(new Error('Signature not found'));
          return Promise.resolve(matches);
        },
        enabled: !!signatureHash,
        staleTime: Infinity,
        retry: false,
      }
    : {
        queryKey: ['dlq-signature-root-cause-matches', namespaceId, signatureHash],
        queryFn: () => dlqSignaturesApi.getRootCauseMatches(namespaceId!, signatureHash!),
        enabled: !!namespaceId && !!signatureHash,
        staleTime: 30_000,
      };

  return useQuery(options);
}

/**
 * Hook for fetching prior versions of a failure signature's operational knowledge.
 */
export function useKnowledgeHistory(namespaceId?: string, signatureHash?: string) {
  const { isDemoMode } = useDemoContext();

  return useQuery({
    queryKey: ['dlq-knowledge-history', namespaceId, signatureHash],
    queryFn: () => dlqSignaturesApi.getKnowledgeHistory(namespaceId!, signatureHash!),
    enabled: !isDemoMode && !!namespaceId && !!signatureHash,
    staleTime: 30_000,
  });
}

interface SignatureLifecycleMutationVars {
  namespaceId: string;
  signatureHash: string;
  notes?: string;
}

function useSignatureLifecycleMutation(target: 'Resolved' | 'Reopened' | 'Suppressed' | 'Archived') {
  const queryClient = useQueryClient();
  const { isDemoMode } = useDemoContext();

  return useMutation({
    mutationFn: ({ namespaceId, signatureHash, notes }: SignatureLifecycleMutationVars) =>
      isDemoMode
        ? rejectDemoModeMutation()
        : dlqSignaturesApi.updateSignatureStatus(namespaceId, signatureHash, target, notes),
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: ['dlq-signatures', variables.namespaceId] });
      queryClient.invalidateQueries({
        queryKey: ['dlq-signature-detail', variables.namespaceId, variables.signatureHash],
      });
      queryClient.invalidateQueries({
        queryKey: ['dlq-signature-timeline', variables.namespaceId, variables.signatureHash],
      });
      toast.success(`Signature marked ${target.toLowerCase()}`);
    },
    onError: (error: unknown) => {
      const err = error as { response?: { data?: { message?: string; detail?: string } }; message?: string };
      const msg =
        err?.response?.data?.detail ||
        err?.response?.data?.message ||
        err?.message ||
        'Failed to update signature status';
      toast.error(msg);
    },
  });
}

/** Marks a failure signature as Resolved. Valid from Active or Reopened. */
export function useResolveSignature() {
  return useSignatureLifecycleMutation('Resolved');
}

/** Reopens a Resolved or Suppressed failure signature. */
export function useReopenSignature() {
  return useSignatureLifecycleMutation('Reopened');
}

/** Suppresses a failure signature. Valid from Active, Resolved, or Reopened. */
export function useSuppressSignature() {
  return useSignatureLifecycleMutation('Suppressed');
}

/** Archives a failure signature. Terminal — valid from any non-Archived status. */
export function useArchiveSignature() {
  return useSignatureLifecycleMutation('Archived');
}

interface UpsertKnowledgeMutationVars {
  namespaceId: string;
  signatureHash: string;
  request: UpsertKnowledgeRequest;
}

function invalidateKnowledgeQueries(
  queryClient: ReturnType<typeof useQueryClient>,
  namespaceId: string,
  signatureHash: string,
) {
  queryClient.invalidateQueries({ queryKey: ['dlq-signatures', namespaceId] });
  queryClient.invalidateQueries({ queryKey: ['dlq-signature-detail', namespaceId, signatureHash] });
  queryClient.invalidateQueries({ queryKey: ['dlq-signature-timeline', namespaceId, signatureHash] });
  queryClient.invalidateQueries({ queryKey: ['dlq-knowledge-history', namespaceId, signatureHash] });
}

/** Creates or updates a failure signature's operational knowledge. */
export function useUpsertKnowledge() {
  const queryClient = useQueryClient();
  const { isDemoMode } = useDemoContext();

  return useMutation({
    mutationFn: ({ namespaceId, signatureHash, request }: UpsertKnowledgeMutationVars) =>
      isDemoMode
        ? rejectDemoModeMutation()
        : dlqSignaturesApi.upsertKnowledge(namespaceId, signatureHash, request),
    onSuccess: (_, variables) => {
      invalidateKnowledgeQueries(queryClient, variables.namespaceId, variables.signatureHash);
      toast.success('Knowledge saved');
    },
    onError: (error: unknown) => {
      const err = error as { response?: { data?: { message?: string; detail?: string } }; message?: string };
      const msg =
        err?.response?.data?.detail ||
        err?.response?.data?.message ||
        err?.message ||
        'Failed to save knowledge';
      toast.error(msg);
    },
  });
}

interface MarkForReviewMutationVars {
  namespaceId: string;
  signatureHash: string;
  reviewDueAt: string;
}

/** Marks a failure signature's knowledge as needing review by a given date. */
export function useMarkForReview() {
  const queryClient = useQueryClient();
  const { isDemoMode } = useDemoContext();

  return useMutation({
    mutationFn: ({ namespaceId, signatureHash, reviewDueAt }: MarkForReviewMutationVars) =>
      isDemoMode
        ? rejectDemoModeMutation()
        : dlqSignaturesApi.markForReview(namespaceId, signatureHash, reviewDueAt),
    onSuccess: (_, variables) => {
      invalidateKnowledgeQueries(queryClient, variables.namespaceId, variables.signatureHash);
      toast.success('Marked for review');
    },
    onError: (error: unknown) => {
      const err = error as { response?: { data?: { message?: string; detail?: string } }; message?: string };
      const msg =
        err?.response?.data?.detail ||
        err?.response?.data?.message ||
        err?.message ||
        'Failed to mark for review';
      toast.error(msg);
    },
  });
}
