import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { insightsApi } from '@/lib/api/insights';
import { GetInsightsParams } from '@/lib/api/types';
import toast from 'react-hot-toast';

export function useInsights(params: GetInsightsParams) {
  return useQuery({
    queryKey: ['insights', params],
    queryFn: () => insightsApi.list(params),
    enabled: !!params.namespaceId,
    retry: false, // Don't retry 404 errors for missing endpoint
    meta: {
      errorMessage: false, // Suppress automatic error toasts
    },
  });
}

export function useInsight(namespaceId: string, insightId: string) {
  return useQuery({
    queryKey: ['insights', namespaceId, insightId],
    queryFn: () => insightsApi.get(namespaceId, insightId),
    enabled: !!namespaceId && !!insightId,
    retry: false,
    meta: {
      errorMessage: false,
    },
  });
}

export function useInsightsSummary(namespaceId: string, queueOrTopicName: string) {
  return useQuery({
    queryKey: ['insights', 'summary', namespaceId, queueOrTopicName],
    queryFn: () => insightsApi.getSummary(namespaceId, queueOrTopicName),
    enabled: !!namespaceId && !!queueOrTopicName,
    retry: false,
    meta: {
      errorMessage: false,
    },
  });
}

export function useDismissInsight() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ namespaceId, insightId }: { namespaceId: string; insightId: string }) =>
      insightsApi.dismiss(namespaceId, insightId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['insights'] });
      toast.success('Insight dismissed');
    },
    onError: () => {
      toast.error('Failed to dismiss insight');
    },
  });
}

export function useResolveInsight() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ namespaceId, insightId }: { namespaceId: string; insightId: string }) =>
      insightsApi.resolve(namespaceId, insightId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['insights'] });
      toast.success('Insight marked as resolved');
    },
    onError: () => {
      toast.error('Failed to resolve insight');
    },
  });
}
