import { useQuery } from '@tanstack/react-query';
import { dlqSignaturesApi } from '@/lib/api/dlqSignatures';

/**
 * Hook for fetching a namespace's DLQ error-cluster signatures. Treats the AI-unavailable
 * response (`available: false`) as a normal state, never as an error — the 200 always carries
 * a well-formed body.
 */
export function useDlqSignatures(namespaceId?: string) {
  const query = useQuery({
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
  });

  return {
    data: query.data,
    loading: query.isLoading,
    error: query.error,
    available: query.data?.available ?? false,
  };
}
