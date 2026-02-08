import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { insightsApi } from '@/lib/api/insights';
import { GetInsightsParams, Message as APIMessage } from '@/lib/api/types';
import { analyzeMessages, isAIAvailable } from '@/lib/ai/analyzer';
import toast from 'react-hot-toast';

/**
 * Hook to fetch AI insights for a namespace/entity
 * 
 * This hook provides AI-powered pattern detection:
 * 1. First tries backend API (if available)
 * 2. Falls back to client-side analysis using message data
 * 
 * TRUST GUARANTEES:
 * - All insights labeled as "ServiceHub Interpretation"
 * - Uncertainty explicitly stated
 * - Evidence always cited
 */
export function useInsights(params: GetInsightsParams) {
  return useQuery({
    queryKey: ['insights', params],
    queryFn: () => insightsApi.list(params),
    enabled: !!params.namespaceId,
    retry: false, // Don't retry 404 errors for missing endpoint
    staleTime: 30000, // Consider insights stale after 30 seconds
    meta: {
      errorMessage: false, // Suppress automatic error toasts
    },
  });
}

/**
 * Hook to get a single insight by ID
 */
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

/**
 * Hook to get insights summary for sidebar badges
 * Uses backend API first, falls back to empty if unavailable
 */
export function useInsightsSummary(namespaceId: string, queueOrTopicName: string) {
  return useQuery({
    queryKey: ['insights', 'summary', namespaceId, queueOrTopicName],
    queryFn: () => insightsApi.getSummary(namespaceId, queueOrTopicName),
    enabled: !!namespaceId && !!queueOrTopicName,
    retry: false,
    staleTime: 30000,
    meta: {
      errorMessage: false,
    },
  });
}

/**
 * Hook to perform client-side AI analysis on messages
 * 
 * This is the main hook for AI pattern detection when backend is unavailable.
 * It analyzes the provided messages and generates insights.
 * 
 * @param messages - Array of messages to analyze
 * @param context - Analysis context (namespace, entity info)
 * @param enabled - Whether analysis should run
 */
export function useClientSideInsights(
  messages: APIMessage[] | undefined,
  context: {
    namespaceId: string;
    entityName: string;
    subscriptionName?: string;
    entityType: 'queue' | 'topic';
  },
  enabled: boolean = true
) {
  return useQuery({
    queryKey: ['insights', 'client-side', context.namespaceId, context.entityName, messages?.length],
    queryFn: () => {
      if (!messages || messages.length === 0) {
        return [];
      }
      
      // Perform client-side analysis
      return analyzeMessages(messages, context);
    },
    enabled: enabled && !!messages && messages.length > 0 && isAIAvailable(),
    staleTime: 60000, // Client-side insights stay fresh for 1 minute
    meta: {
      errorMessage: false,
    },
  });
}

/**
 * Hook to dismiss an insight
 */
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
      toast.error('Failed to dismiss insight. Client-side insights cannot be persisted.', {
        duration: 5000,
      });
    },
  });
}

/**
 * Hook to resolve an insight
 */
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
      toast.error('Failed to resolve insight. Client-side insights cannot be persisted.', {
        duration: 5000,
      });
    },
  });
}

/**
 * Hook to check if AI is available
 */
export function useAIAvailability() {
  return useQuery({
    queryKey: ['ai', 'availability'],
    queryFn: () => insightsApi.isAvailable(),
    staleTime: 60000, // Check availability every minute
  });
}
