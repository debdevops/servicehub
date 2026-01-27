import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { messagesApi } from '@/lib/api/messages';
import { GetMessagesParams } from '@/lib/api/types';
import toast from 'react-hot-toast';

/**
 * Sanitize queue name to ensure $deadletterqueue suffix is not passed
 */
function sanitizeQueueName(name: string): string {
  return name.replace(/\/?\$deadletterqueue$/i, '');
}

export function useMessages(params: GetMessagesParams) {
  // Sanitize the queue/topic name
  const sanitizedParams = {
    ...params,
    queueOrTopicName: sanitizeQueueName(params.queueOrTopicName),
  };
  
  return useQuery({
    queryKey: ['messages', sanitizedParams],
    queryFn: () => messagesApi.list(sanitizedParams),
    enabled: !!sanitizedParams.namespaceId && !!sanitizedParams.queueOrTopicName,
    staleTime: 2000, // Consider data stale after 2 seconds for near real-time updates
    retry: (failureCount, error: any) => {
      // Don't retry on 404 errors
      if (error?.response?.status === 404) return false;
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
      message: any;
      entityType?: 'queue' | 'topic';
    }) => messagesApi.send(namespaceId, queueOrTopicName, message, entityType),
    onSuccess: async (_, variables) => {
      // Invalidate and refetch ALL related queries for immediate UI update
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['messages'], refetchType: 'active' }),
        queryClient.invalidateQueries({ queryKey: ['queues', variables.namespaceId], refetchType: 'active' }),
        queryClient.invalidateQueries({ queryKey: ['subscriptions', variables.namespaceId], refetchType: 'active' }),
      ]);
      toast.success('Message sent successfully');
    },
    onError: (error: any) => {
      const errorMsg = error?.response?.data?.message || error?.message || 'Failed to send message';
      toast.error(errorMsg);
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
      // Invalidate and refetch ALL related queries for immediate UI update
      // This includes both DLQ and active message lists + counts
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['messages'], refetchType: 'active' }),
        queryClient.invalidateQueries({ queryKey: ['queues', variables.namespaceId], refetchType: 'active' }),
        queryClient.invalidateQueries({ queryKey: ['subscriptions', variables.namespaceId], refetchType: 'active' }),
      ]);
      toast.success('Message replayed successfully');
    },
    onError: (error: any) => {
      // Check if it's a 404 - feature not implemented yet
      if (error?.response?.status === 404) {
        toast.error('Replay feature is not yet available in the API', {
          duration: 4000,
          icon: '🚧',
        });
      } else {
        const errorMsg = error?.response?.data?.message || error?.message || 'Failed to replay message';
        toast.error(errorMsg);
      }
    },
  });
}

/* PURGE HOOK DISABLED - Azure Service Bus Limitation
 * The Service Bus SDK doesn't support direct access to messages by sequence number.
 * Scanning through messages times out for large queues.
 * Re-enable if Microsoft adds targeted message deletion support.
 *
export function usePurgeMessage() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ 
      namespaceId, 
      sequenceNumber, 
      entityName, 
      subscriptionName,
      fromDeadLetter 
    }: { 
      namespaceId: string; 
      sequenceNumber: number; 
      entityName: string;
      subscriptionName?: string;
      fromDeadLetter?: boolean;
    }) => 
      messagesApi.purge(namespaceId, sequenceNumber, entityName, subscriptionName, fromDeadLetter),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['messages'] });
      toast.success('Message purged successfully');
    },
    onError: (error: any) => {
      // Check if it's a 404 - feature not implemented yet
      if (error?.response?.status === 404) {
        toast.error('Purge feature is not yet available in the API', {
          duration: 4000,
          icon: '🚧',
        });
      } else {
        const errorMsg = error?.response?.data?.message || error?.message || 'Failed to purge message';
        toast.error(errorMsg);
      }
    },
  });
}
*/
