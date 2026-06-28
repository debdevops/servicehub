import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { messagesApi } from '@/lib/api/messages';
import { GetMessagesParams, PaginatedResponse, Message, ApiError } from '@/lib/api/types';
import { useDemoContext } from '@/lib/demo/DemoContext';
import { getMockMessages } from '@/lib/demo/mockProviders';
import toast from 'react-hot-toast';

/**
 * Sanitize queue/topic name to ensure $deadletterqueue suffix is not passed
 */
function sanitizeQueueName(name: string): string {
  return name.replace(/\/?\$deadletterqueue$/i, '');
}

export function useMessages(params: GetMessagesParams & { autoRefresh?: boolean }) {
  const { isDemoMode, cloudProvider } = useDemoContext();

  const sanitizedName = sanitizeQueueName(params.queueOrTopicName);

  const options = isDemoMode && cloudProvider
    ? {
        queryKey: ['messages', 'demo', cloudProvider, sanitizedName, params.queueType, params.skip] as const,
        queryFn: (): Promise<PaginatedResponse<Message>> => Promise.resolve(
          getMockMessages(
            cloudProvider,
            sanitizedName,
            params.queueType ?? 'active',
            params.skip ?? 0,
            params.take ?? 50,
          )
        ),
        enabled: !!sanitizedName,
        staleTime: Infinity as number,
        refetchInterval: false as const,
        refetchIntervalInBackground: false,
        retry: false as const,
      }
    : {
        queryKey: ['messages', { ...params, queueOrTopicName: sanitizedName }] as const,
        queryFn: async (): Promise<PaginatedResponse<Message>> => {
          try {
            return await messagesApi.list({ ...params, queueOrTopicName: sanitizedName });
          } catch (error: unknown) {
            const status = (error as ApiError)?.response?.status;
            if (status === 404 || status === 502 || status === 503) {
              return {
                items: [],
                totalCount: 0,
                page: 1,
                pageSize: params.take ?? 50,
                hasNextPage: false,
                hasPreviousPage: false,
              };
            }
            throw error;
          }
        },
        enabled: !!params.namespaceId && !!sanitizedName,
        staleTime: 10_000,
        refetchInterval: params.autoRefresh !== false ? 30_000 : (false as const),
        refetchIntervalInBackground: false,
        retry: (failureCount: number, error: ApiError) => {
          if (error?.response?.status === 404) return false;
          if (error?.response?.status === 401 || error?.response?.status === 403) return false;
          if (error?.response?.status === 429) return false;
          if ((error?.response?.status ?? 0) >= 500) return false;
          return failureCount < 2;
        },
        meta: {
          errorMessage: false,
        },
      };

  return useQuery<PaginatedResponse<Message>, ApiError>(
    options as Parameters<typeof useQuery<PaginatedResponse<Message>, ApiError>>[0]
  );
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
      toast.error(errorMsg, { duration: Infinity });
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
      if (error?.response?.status === 404) {
        toast.error('Replay feature is not yet available in the API', {
          duration: 4000,
          icon: '🚧',
        });
      } else {
        const errorMsg = error?.response?.data?.message || error?.message || 'Failed to replay message';
        toast.error(errorMsg, { duration: Infinity });
      }
    },
  });
}
