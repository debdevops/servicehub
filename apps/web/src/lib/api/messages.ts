import { apiClient } from './client';
import { Message, PaginatedResponse, GetMessagesParams } from './types';

export const messagesApi = {
  // GET /api/v1/namespaces/{namespaceId}/queues/{queueName}/messages
  list: async (params: GetMessagesParams): Promise<PaginatedResponse<Message>> => {
    const { namespaceId, queueOrTopicName, ...queryParams } = params;
    
    const response = await apiClient.get<PaginatedResponse<Message>>(
      `/namespaces/${namespaceId}/queues/${queueOrTopicName}/messages`,
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
    }
  ): Promise<void> => {
    await apiClient.post(
      `/namespaces/${namespaceId}/queues/${queueOrTopicName}/messages`,
      message
    );
  },

  // POST /api/v1/namespaces/{namespaceId}/messages/{messageId}/replay
  replay: async (namespaceId: string, messageId: string): Promise<void> => {
    await apiClient.post(`/namespaces/${namespaceId}/messages/${messageId}/replay`);
  },

  // DELETE /api/v1/namespaces/{namespaceId}/messages/{messageId}
  purge: async (namespaceId: string, messageId: string): Promise<void> => {
    await apiClient.delete(`/namespaces/${namespaceId}/messages/${messageId}`);
  },
};
