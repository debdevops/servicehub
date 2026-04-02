import { apiClient } from './client';
import { Message, PaginatedResponse } from './types';

export const scheduledApi = {
  /**
   * Lists scheduled messages in a queue with pagination.
   * GET /api/v1/namespaces/{namespaceId}/queues/{queueName}/scheduled
   */
  listScheduled: async (
    namespaceId: string,
    queueName: string,
    skip = 0,
    take = 100
  ): Promise<PaginatedResponse<Message>> => {
    const response = await apiClient.get<PaginatedResponse<Message>>(
      `/namespaces/${namespaceId}/queues/${queueName}/scheduled`,
      { params: { skip, take } }
    );
    return response.data;
  },

  /**
   * Cancels a scheduled message by its sequence number.
   * DELETE /api/v1/namespaces/{namespaceId}/queues/{queueName}/scheduled/{sequenceNumber}
   */
  cancelScheduled: async (
    namespaceId: string,
    queueName: string,
    sequenceNumber: number
  ): Promise<void> => {
    await apiClient.delete(
      `/namespaces/${namespaceId}/queues/${queueName}/scheduled/${sequenceNumber}`
    );
  },
};
