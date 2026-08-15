import { useQuery, useMutation, useQueryClient, UseQueryOptions } from '@tanstack/react-query';
import { messagesApi } from '../lib/api/messages';
import { GetMessagesParams, PaginatedResponse, Message, ApiError } from '../lib/api/types';
import { extractApiError } from '../lib/api/errors';
import { useDemoContext, rejectDemoModeMutation } from '../lib/demo/DemoContext';
import { getMockMessages } from '../lib/demo/mockProviders';
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

  const options: UseQueryOptions<PaginatedResponse<Message>, ApiError> = isDemoMode && cloudProvider
    ? {
        queryKey: ['messages', 'demo', cloudProvider, sanitizedName, params.queueType, params.skip],
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
        staleTime: Infinity,
        refetchInterval: false,
        refetchIntervalInBackground: false,
        retry: false,
      }
    : {
        queryKey: ['messages', { ...params, queueOrTopicName: sanitizedName }],
        queryFn: async (): Promise<PaginatedResponse<Message>> => {
          try {
            return await messagesApi.list({ ...params, queueOrTopicName: sanitizedName });
          } catch (error: unknown) {
            const status = (error as ApiError)?.response?.status;
            // Only 404 ("this queue/topic doesn't exist") is a legitimate empty state.
            // 502/503 mean the provider call itself failed (e.g. an unsupported GCP
            // Pub/Sub subscription type, or a disabled provider flag) — swallowing those
            // into an empty list is indistinguishable from "no messages" and hides an
            // actionable backend error from the user. Let them propagate so the page's
            // error state (which reads the ProblemDetails "detail") can show it.
            if (status === 404) {
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
        refetchInterval: params.autoRefresh !== false ? 30_000 : false,
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

  return useQuery(options);
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
  const { isDemoMode } = useDemoContext();

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
    }) =>
      isDemoMode
        ? rejectDemoModeMutation()
        : messagesApi.send(namespaceId, queueOrTopicName, message, entityType),
    onSuccess: async (_, variables) => {
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: ['messages', { namespaceId: variables.namespaceId, queueOrTopicName: variables.queueOrTopicName }],
          exact: false,
          refetchType: 'active',
        }),
        queryClient.invalidateQueries({ queryKey: ['queues', variables.namespaceId], refetchType: 'active' }),
        queryClient.invalidateQueries({ queryKey: ['subscriptions', variables.namespaceId], refetchType: 'active' }),
        queryClient.invalidateQueries({ queryKey: ['namespace-stats', variables.namespaceId], refetchType: 'active' }),
      ]);
      toast.success('Message sent successfully');
    },
    onError: (error: ApiError) => {
      toast.error(extractApiError(error, 'Failed to send message'), { duration: 8000 });
    },
  });
}

export function useReplayMessage() {
  const queryClient = useQueryClient();
  const { isDemoMode } = useDemoContext();

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
      isDemoMode
        ? rejectDemoModeMutation()
        : messagesApi.replay(namespaceId, sequenceNumber, entityName, subscriptionName),
    onSuccess: async (_, variables) => {
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: ['messages', { namespaceId: variables.namespaceId, queueOrTopicName: variables.entityName }],
          exact: false,
          refetchType: 'active',
        }),
        queryClient.invalidateQueries({ queryKey: ['queues', variables.namespaceId], refetchType: 'active' }),
        queryClient.invalidateQueries({ queryKey: ['subscriptions', variables.namespaceId], refetchType: 'active' }),
        queryClient.invalidateQueries({ queryKey: ['namespace-stats', variables.namespaceId], refetchType: 'active' }),
        // Recovery Ledger otherwise only refreshes on its own 60s refetchInterval — see
        // useBulkOperations.ts for the same fix on the bulk-operation completion path.
        queryClient.invalidateQueries({ queryKey: ['recovery-operations'] }),
        queryClient.invalidateQueries({ queryKey: ['recovery-entries'] }),
      ]);
      toast.success('Message replayed successfully');
    },
    onError: (error: ApiError) => {
      // A 404 means the message is gone, not that replay is unimplemented — replay has
      // shipped for many releases. The old "not yet available" copy read as an unfinished
      // product at the exact moment a user was exercising the flagship operation.
      const fallback = error?.response?.status === 404
        ? 'Message not found — it may have been consumed, expired, or already replayed.'
        : 'Failed to replay message';
      toast.error(extractApiError(error, fallback), { duration: 8000 });
    },
  });
}

export function usePurgeMessage() {
  const queryClient = useQueryClient();
  const { isDemoMode } = useDemoContext();

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
      isDemoMode
        ? rejectDemoModeMutation()
        : messagesApi.purge(namespaceId, sequenceNumber, entityName, subscriptionName, fromDeadLetter),
    onSuccess: async (_, variables) => {
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: ['messages', { namespaceId: variables.namespaceId, queueOrTopicName: variables.entityName }],
          exact: false,
          refetchType: 'active',
        }),
        queryClient.invalidateQueries({ queryKey: ['queues', variables.namespaceId], refetchType: 'active' }),
        queryClient.invalidateQueries({ queryKey: ['subscriptions', variables.namespaceId], refetchType: 'active' }),
        // Recovery Ledger otherwise only refreshes on its own 60s refetchInterval — see
        // useBulkOperations.ts for the same fix on the bulk-operation completion path.
        queryClient.invalidateQueries({ queryKey: ['recovery-operations'] }),
        queryClient.invalidateQueries({ queryKey: ['recovery-entries'] }),
      ]);
      toast.success('Message purged successfully');
    },
    onError: (error: ApiError) => {
      const fallback = error?.response?.status === 404
        ? 'Message not found — it may have been consumed, expired, or already purged.'
        : 'Failed to purge message';
      toast.error(extractApiError(error, fallback), { duration: 8000 });
    },
  });
}
