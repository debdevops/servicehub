import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { dlqSignaturesApi } from '../lib/api/dlqSignatures';
import { useDemoContext, rejectDemoModeMutation } from '../lib/demo/DemoContext';
import toast from 'react-hot-toast';

/**
 * Hook for fetching a namespace's DLQ error-cluster signatures. Treats the AI-unavailable
 * response (`available: false`) as a normal state, never as an error — the 200 always carries
 * a well-formed body.
 */
export function useDlqSignatures(namespaceId?: string) {
  const { isDemoMode } = useDemoContext();

  const query = useQuery({
    queryKey: ['dlq-signatures', namespaceId],
    queryFn: () => dlqSignaturesApi.getSignatures(namespaceId!),
    enabled: !isDemoMode && !!namespaceId,
    staleTime: 60_000,
    retry: (failureCount, error: unknown) => {
      const err = error as { response?: { status?: number } };
      if (err?.response?.status === 404) return false;
      if (err?.response?.status === 429) return false;
      return failureCount < 2;
    },
  });

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
  const { isDemoMode } = useDemoContext();

  return useQuery({
    queryKey: ['dlq-signature-detail', namespaceId, signatureHash],
    queryFn: () => dlqSignaturesApi.getSignatureDetail(namespaceId!, signatureHash!),
    enabled: !isDemoMode && !!namespaceId && !!signatureHash,
    staleTime: 30_000,
  });
}

/**
 * Hook for fetching a failure signature's lifecycle timeline.
 */
export function useSignatureTimeline(namespaceId?: string, signatureHash?: string) {
  const { isDemoMode } = useDemoContext();

  return useQuery({
    queryKey: ['dlq-signature-timeline', namespaceId, signatureHash],
    queryFn: () => dlqSignaturesApi.getSignatureTimeline(namespaceId!, signatureHash!),
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
