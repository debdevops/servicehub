import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { messagesApi } from '@/lib/api/messages';
import { GetMessagesParams } from '@/lib/api/types';
import toast from 'react-hot-toast';

export function useMessages(params: GetMessagesParams) {
  return useQuery({
    queryKey: ['messages', params],
    queryFn: () => messagesApi.list(params),
    enabled: !!params.namespaceId && !!params.queueOrTopicName, // Don't fetch if no namespace selected
  });
}

export function useMessage(namespaceId: string, messageId: string) {
  return useQuery({
    queryKey: ['messages', namespaceId, messageId],
    queryFn: () => messagesApi.get(namespaceId, messageId),
    enabled: !!namespaceId && !!messageId,
  });
}

export function useSendMessage() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ 
      namespaceId, 
      queueOrTopicName, 
      message 
    }: { 
      namespaceId: string; 
      queueOrTopicName: string; 
      message: any 
    }) => messagesApi.send(namespaceId, queueOrTopicName, message),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['messages'] });
      toast.success('Message sent successfully');
    },
    onError: () => {
      toast.error('Failed to send message');
    },
  });
}

export function useReplayMessage() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ namespaceId, messageId }: { namespaceId: string; messageId: string }) => 
      messagesApi.replay(namespaceId, messageId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['messages'] });
      toast.success('Message replayed successfully');
    },
    onError: () => {
      toast.error('Failed to replay message');
    },
  });
}

export function usePurgeMessage() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ namespaceId, messageId }: { namespaceId: string; messageId: string }) => 
      messagesApi.purge(namespaceId, messageId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['messages'] });
      toast.success('Message purged successfully');
    },
    onError: () => {
      toast.error('Failed to purge message');
    },
  });
}
