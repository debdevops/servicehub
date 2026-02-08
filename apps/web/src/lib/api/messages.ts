import { apiClient } from './client';
import { Message, PaginatedResponse, GetMessagesParams } from './types';

/**
 * Sanitize queue/topic name to remove $deadletterqueue suffix
 * This handles cases where URLs incorrectly include $deadletterqueue in the path
 * The correct way to access DLQ is via queueType=deadletter parameter
 */
function sanitizeEntityName(name: string): string {
  // Remove $deadletterqueue suffix if present (with or without leading /)
  return name.replace(/\/?\$deadletterqueue$/i, '');
}

export const messagesApi = {
  // GET /api/v1/namespaces/{namespaceId}/queues/{queueName}/messages
  // OR /api/v1/namespaces/{namespaceId}/topics/{topicName}/messages
  list: async (params: GetMessagesParams): Promise<PaginatedResponse<Message>> => {
    const { namespaceId, entityType = 'queue', ...queryParams } = params;
    
    // Sanitize entity name to remove any $deadletterqueue suffix
    const queueOrTopicName = sanitizeEntityName(params.queueOrTopicName);
    
    if (entityType === 'topic' && queueOrTopicName.includes('/subscriptions/')) {
      const [topicName, subscriptionName] = queueOrTopicName.split('/subscriptions/');
      const response = await apiClient.get<PaginatedResponse<Message>>(
        `/namespaces/${namespaceId}/topics/${topicName}/subscriptions/${subscriptionName}/messages`,
        { params: queryParams }
      );
      return response.data;
    }

    const entityPath = entityType === 'topic' ? 'topics' : 'queues';
    const response = await apiClient.get<PaginatedResponse<Message>>(
      `/namespaces/${namespaceId}/${entityPath}/${queueOrTopicName}/messages`,
      { params: queryParams }
    );
    
    return response.data;
  },

  // GET /api/v1/namespaces/{namespaceId}/messages/{messageId}
  get: async (namespaceId: string, messageId: string): Promise<Message> => {
    const response = await apiClient.get<Message>(
      `/namespaces/${namespaceId}/messages/${messageId}`
    );
    return response.data;
  },

  // POST /api/v1/namespaces/{namespaceId}/queues/{queueName}/messages (send)
  // OR /api/v1/namespaces/{namespaceId}/topics/{topicName}/messages (send)
  send: async (
    namespaceId: string,
    queueOrTopicName: string,
    message: {
      body: string;
      contentType?: string;
      properties?: Record<string, any>;
      sessionId?: string;
      correlationId?: string;
      timeToLive?: number;
      scheduledEnqueueTime?: string;
    },
    entityType: 'queue' | 'topic' = 'queue'
  ): Promise<void> => {
    const entityPath = entityType === 'topic' ? 'topics' : 'queues';
    
    // Map 'properties' to 'applicationProperties' to match API contract
    const payload = {
      body: message.body,
      contentType: message.contentType,
      applicationProperties: message.properties,
      sessionId: message.sessionId,
      correlationId: message.correlationId,
      timeToLiveSeconds: message.timeToLive,
      scheduledEnqueueTimeUtc: message.scheduledEnqueueTime,
    };
    
    await apiClient.post(
      `/namespaces/${namespaceId}/${entityPath}/${queueOrTopicName}/messages`,
      payload
    );
  },

  // POST /api/v1/messages/replay
  replay: async (
    namespaceId: string, 
    sequenceNumber: number,
    entityName: string,
    subscriptionName?: string
  ): Promise<void> => {
    await apiClient.post('/messages/replay', null, {
      params: {
        namespaceId,
        sequenceNumber,
        entityName,
        subscriptionName
      }
    });
  },

  /* PURGE API DISABLED - Azure Service Bus Limitation
   * The Service Bus SDK doesn't support direct message deletion by sequence number.
   * Re-enable if Microsoft adds targeted message deletion support.
   *
  // DELETE /api/v1/messages/purge
  purge: async (
    namespaceId: string, 
    sequenceNumber: number,
    entityName: string,
    subscriptionName?: string,
    fromDeadLetter?: boolean
  ): Promise<void> => {
    await apiClient.delete('/messages/purge', {
      params: {
        namespaceId,
        sequenceNumber,
        entityName,
        subscriptionName,
        fromDeadLetter
      }
    });
  },
  */

  // POST /api/v1/namespaces/{namespaceId}/queues/{queueName}/deadletter
  // Moves messages to the dead-letter queue for testing
  deadLetter: async (
    namespaceId: string,
    queueOrTopicName: string,
    messageCount: number = 1,
    reason: string = 'ManualDeadLetter',
    errorDescription?: string,
    entityType: 'queue' | 'topic' = 'queue',
    subscriptionName?: string
  ): Promise<{ deadLetteredCount: number; reason: string }> => {
    const params = new URLSearchParams({
      messageCount: messageCount.toString(),
      reason,
      ...(errorDescription && { errorDescription }),
    });
    
    let url: string;
    if (entityType === 'topic' && subscriptionName) {
      url = `/namespaces/${namespaceId}/topics/${queueOrTopicName}/subscriptions/${subscriptionName}/deadletter`;
    } else {
      url = `/namespaces/${namespaceId}/queues/${queueOrTopicName}/deadletter`;
    }
    
    const response = await apiClient.post<{ deadLetteredCount: number; reason: string }>(
      `${url}?${params.toString()}`
    );
    return response.data;
  },
};
