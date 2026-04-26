import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { messagesApi } from '@/lib/api/messages';
import { GetMessagesParams, ApiError } from '@/lib/api/types';
import toast from 'react-hot-toast';

/**
 * Sanitize queue name to ensure $deadletterqueue suffix is not passed
 */
function sanitizeQueueName(name: string): string {
  return name.replace(/\/?\$deadletterqueue$/i, '');
}

export function useMessages(params: GetMessagesParams & { autoRefresh?: boolean }) {
  // Sanitize the queue/topic name
  const sanitizedParams = {
    ...params,
    queueOrTopicName: sanitizeQueueName(params.queueOrTopicName),
  };
  
  return useQuery({
    queryKey: ['messages', sanitizedParams],
    queryFn: async () => {
      try {
        return await messagesApi.list(sanitizedParams);
      } catch (error: unknown) {
        // For 404s or Service Bus connectivity errors, return empty result
        // instead of throwing. This prevents toast spam from background polling
        // when the Service Bus namespace is unavailable.
        const status = (error as ApiError)?.response?.status;
        if (status === 404 || status === 502 || status === 503) {
          return { items: [], totalCount: 0, hasMore: false };
        }
        throw error;
      }
    },
    enabled: !!sanitizedParams.namespaceId && !!sanitizedParams.queueOrTopicName,
    staleTime: 10_000,
    refetchInterval: params.autoRefresh !== false ? 30_000 : false,
    refetchIntervalInBackground: false,
    retry: (failureCount, error: ApiError) => {
      // Don't retry on 404 errors (entity not found)
      if (error?.response?.status === 404) return false;
      // Don't retry on 401/403 (auth errors)
      if (error?.response?.status === 401 || error?.response?.status === 403) return false;
      // Don't retry on rate-limit errors — retrying makes the storm worse
      if (error?.response?.status === 429) return false;
      // Don't retry on Service Bus connectivity errors
      if ((error?.response?.status ?? 0) >= 500) return false;
      return failureCount < 2;
    },
    meta: {
      errorMessage: false, // Suppress automatic error toasts for 404s
    },
  });
}

export function useMessage(namespaceId: string, messageId: string) {
  return useQuery({
    queryKey: ['messages', namespaceId, messageId],
    queryFn: () => messagesApi.get(namespaceId, messageId),
    enabled: !!namespaceId && !!messageId,
    retry: false,
  });
}

export function useSendMessage() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ 
      namespaceId, 
      queueOrTopicName, 
      message,
      entityType = 'queue'
    }: { 
      namespaceId: string; 
      queueOrTopicName: string; 
      message: {
        body: string;
        contentType?: string;
        properties?: Record<string, unknown>;
        sessionId?: string;
        correlationId?: string;
        timeToLive?: number;
        scheduledEnqueueTime?: string;
      };
      entityType?: 'queue' | 'topic';
    }) => messagesApi.send(namespaceId, queueOrTopicName, message, entityType),
    onSuccess: async (_, variables) => {
      // Invalidate specific queue/topic messages and counts (not all messages)
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: ['messages', { namespaceId: variables.namespaceId, queueOrTopicName: variables.queueOrTopicName }],
          exact: false,
          refetchType: 'active',
        }),
        queryClient.invalidateQueries({ queryKey: ['queues', variables.namespaceId], refetchType: 'active' }),
        queryClient.invalidateQueries({ queryKey: ['subscriptions', variables.namespaceId], refetchType: 'active' }),
      ]);
      toast.success('Message sent successfully');
    },
    onError: (error: ApiError) => {
      const errorMsg = error?.response?.data?.message || error?.message || 'Failed to send message';
      toast.error(errorMsg, {
        duration: Infinity, // Force user to acknowledge critical failure
      });
    },
  });
}

export function useReplayMessage() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ 
      namespaceId, 
      sequenceNumber, 
      entityName, 
      subscriptionName 
    }: { 
      namespaceId: string; 
      sequenceNumber: number; 
      entityName: string;
      subscriptionName?: string;
    }) => 
      messagesApi.replay(namespaceId, sequenceNumber, entityName, subscriptionName),
    onSuccess: async (_, variables) => {
      // Invalidate specific entity messages and counts (not all messages)
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: ['messages', { namespaceId: variables.namespaceId, queueOrTopicName: variables.entityName }],
          exact: false,
          refetchType: 'active',
        }),
        queryClient.invalidateQueries({ queryKey: ['queues', variables.namespaceId], refetchType: 'active' }),
        queryClient.invalidateQueries({ queryKey: ['subscriptions', variables.namespaceId], refetchType: 'active' }),
      ]);
      toast.success('Message replayed successfully');
    },
    onError: (error: ApiError) => {
      // Check if it's a 404 - feature not implemented yet
      if (error?.response?.status === 404) {
        toast.error('Replay feature is not yet available in the API', {
          duration: 4000,
          icon: '🚧',
        });
      } else {
        const errorMsg = error?.response?.data?.message || error?.message || 'Failed to replay message';
        toast.error(errorMsg, {
          duration: Infinity, // Force user to acknowledge critical failure
        });
      }
    },
  });
}


