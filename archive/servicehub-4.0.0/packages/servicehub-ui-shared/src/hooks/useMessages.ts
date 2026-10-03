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

/**
 * Peek-based list APIs (Azure/AWS/GCP all lack a "list newest first" primitive) return
 * messages starting from the head of the queue, capped at `take`. A message just sent to
 * a queue that already has more than `take` active messages ahead of it is never included
 * in the fetched page at all, so no amount of client-side sorting can surface it. To give
 * immediate, provider-agnostic feedback we track "just sent" messages ourselves and merge
 * them into the displayed list until the real message is confirmed present in a fetch (or
 * the entry goes stale).
 */
const OPTIMISTIC_TTL_MS = 10 * 60 * 1000;
const OPTIMISTIC_CAP = 50;

interface OptimisticSentMessage {
  message: Message;
  sentAt: number;
}

// Sending targets a queue or a plain topic name; the messages *list* for a topic is
// scoped to one subscription (`topicName/subscriptions/subName`). Key optimistic entries
// by the send target so every subscription of a topic can show the same "just sent" entry.
function toSendTargetName(entityType: 'queue' | 'topic' | undefined, queueOrTopicName: string): string {
  if (entityType !== 'topic') return queueOrTopicName;
  const idx = queueOrTopicName.indexOf('/subscriptions/');
  return idx === -1 ? queueOrTopicName : queueOrTopicName.slice(0, idx);
}

function optimisticMessagesKey(namespaceId: string, entityType: 'queue' | 'topic' | undefined, queueOrTopicName: string) {
  return ['messages-optimistic', namespaceId, entityType || 'queue', toSendTargetName(entityType, queueOrTopicName)] as const;
}

// Best-effort match between a locally-tracked "just sent" message and a real message
// that came back from a peek — used to drop the optimistic entry once the real one is
// actually visible, so a shallow queue never shows the same message twice.
function isSameSentMessage(optimistic: Message, real: Message): boolean {
  if (optimistic.body !== real.body) return false;
  if ((optimistic.correlationId || null) !== (real.correlationId || null)) return false;
  if ((optimistic.sessionId || null) !== (real.sessionId || null)) return false;
  // The broker/provider SDK adds its own properties on send (e.g. Azure Service Bus stamps
  // a W3C "Diagnostic-Id" trace-context property), so the real message's applicationProperties
  // is a superset of what the client sent, not an exact match. Require every property the
  // client actually sent to be present with the same value; extra broker-added keys are fine.
  const optimisticProps = optimistic.applicationProperties || {};
  const realProps = real.applicationProperties || {};
  const propsMatch = Object.entries(optimisticProps).every(
    ([key, value]) => realProps[key] === value
  );
  if (!propsMatch) return false;
  // The real message can't have been enqueued before we sent it (allow a few seconds of
  // clock skew between the browser and the broker).
  return new Date(real.enqueuedTime).getTime() >= new Date(optimistic.enqueuedTime).getTime() - 5_000;
}

/**
 * Messages sent from this browser for the given queue/topic that haven't yet been
 * confirmed present in a real fetch — merge these into a message list so a freshly sent
 * message shows up immediately regardless of where the peek window happens to land.
 */
export function useOptimisticSentMessages(
  namespaceId: string,
  queueOrTopicName: string,
  entityType: 'queue' | 'topic' = 'queue'
): Message[] {
  const { data } = useQuery<OptimisticSentMessage[]>({
    queryKey: optimisticMessagesKey(namespaceId, entityType, queueOrTopicName),
    queryFn: () => [],
    enabled: false,
    initialData: [],
    staleTime: Infinity,
  });

  const now = Date.now();
  return (data ?? [])
    .filter(entry => now - entry.sentAt < OPTIMISTIC_TTL_MS)
    .map(entry => entry.message);
}

export function useMessages(params: GetMessagesParams & { autoRefresh?: boolean }) {
  const { isDemoMode, cloudProvider } = useDemoContext();
  const queryClient = useQueryClient();

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
            const result = await messagesApi.list({ ...params, queueOrTopicName: sanitizedName });

            // Reconcile: drop any optimistic "just sent" entry that this fetch just proved
            // is now really present, so it doesn't render twice.
            if ((params.skip ?? 0) === 0 && params.queueType !== 'deadletter' && result.items.length > 0) {
              const optimisticKey = optimisticMessagesKey(params.namespaceId, params.entityType, sanitizedName);
              queryClient.setQueryData<OptimisticSentMessage[]>(optimisticKey, (old = []) =>
                old.filter(entry => !result.items.some(real => isSameSentMessage(entry.message, real)))
              );
            }

            return result;
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
      // Scheduled sends don't become active until their scheduled time, so there's
      // nothing to show at the top of the (active) list yet.
      if (!variables.message.scheduledEnqueueTime) {
        const now = new Date();
        const optimisticMessage: Message = {
          messageId: `optimistic-${now.getTime()}-${Math.random().toString(36).slice(2)}`,
          sequenceNumber: 0,
          enqueuedTime: now.toISOString(),
          deliveryCount: 0,
          state: 'Active',
          contentType: variables.message.contentType || 'application/json',
          body: variables.message.body,
          correlationId: variables.message.correlationId ?? null,
          sessionId: variables.message.sessionId ?? null,
          timeToLive: variables.message.timeToLive != null ? String(variables.message.timeToLive) : null,
          applicationProperties: variables.message.properties ?? null,
          isFromDeadLetter: false,
        };
        const optimisticKey = optimisticMessagesKey(variables.namespaceId, variables.entityType, variables.queueOrTopicName);
        queryClient.setQueryData<OptimisticSentMessage[]>(optimisticKey, (old = []) =>
          [{ message: optimisticMessage, sentAt: now.getTime() }, ...old].slice(0, OPTIMISTIC_CAP)
        );
      }

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
        // DLQ History, Failure Signatures, and the Dashboard fleet card otherwise only refresh on
        // their own poll intervals (dlq-signatures has none at all) — a replay changes all three.
        queryClient.invalidateQueries({ queryKey: ['dlq-history'] }),
        queryClient.invalidateQueries({ queryKey: ['dlq-summary', variables.namespaceId] }),
        queryClient.invalidateQueries({ queryKey: ['dlq-signatures', variables.namespaceId] }),
        queryClient.invalidateQueries({ queryKey: ['dlq-signature-detail', variables.namespaceId], exact: false }),
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
        queryClient.invalidateQueries({ queryKey: ['namespace-stats', variables.namespaceId], refetchType: 'active' }),
        // Recovery Ledger otherwise only refreshes on its own 60s refetchInterval — see
        // useBulkOperations.ts for the same fix on the bulk-operation completion path.
        queryClient.invalidateQueries({ queryKey: ['recovery-operations'] }),
        queryClient.invalidateQueries({ queryKey: ['recovery-entries'] }),
        // DLQ History, Failure Signatures, and the Dashboard fleet card otherwise only refresh on
        // their own poll intervals (dlq-signatures has none at all) — a purge changes all three.
        queryClient.invalidateQueries({ queryKey: ['dlq-history'] }),
        queryClient.invalidateQueries({ queryKey: ['dlq-summary', variables.namespaceId] }),
        queryClient.invalidateQueries({ queryKey: ['dlq-signatures', variables.namespaceId] }),
        queryClient.invalidateQueries({ queryKey: ['dlq-signature-detail', variables.namespaceId], exact: false }),
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
