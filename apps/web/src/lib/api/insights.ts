import { apiClient } from './client';
import { AIInsight, GetInsightsParams } from './types';

export const insightsApi = {
  // GET /api/v1/namespaces/{namespaceId}/insights
  list: async (params: GetInsightsParams): Promise<AIInsight[]> => {
    const { namespaceId, ...queryParams } = params;
    
    const response = await apiClient.get<AIInsight[]>(
      `/namespaces/${namespaceId}/insights`,
      { params: queryParams }
    );
    
    return response.data;
  },

  // GET /api/v1/namespaces/{namespaceId}/insights/{insightId}
  get: async (namespaceId: string, insightId: string): Promise<AIInsight> => {
    const response = await apiClient.get<AIInsight>(
      `/namespaces/${namespaceId}/insights/${insightId}`
    );
    return response.data;
  },

  // POST /api/v1/namespaces/{namespaceId}/insights/{insightId}/dismiss
  dismiss: async (namespaceId: string, insightId: string): Promise<void> => {
    await apiClient.post(`/namespaces/${namespaceId}/insights/${insightId}/dismiss`);
  },

  // POST /api/v1/namespaces/{namespaceId}/insights/{insightId}/resolve
  resolve: async (namespaceId: string, insightId: string): Promise<void> => {
    await apiClient.post(`/namespaces/${namespaceId}/insights/${insightId}/resolve`);
  },

  // GET /api/v1/namespaces/{namespaceId}/queues/{queueName}/insights/summary
  getSummary: async (namespaceId: string, queueOrTopicName: string): Promise<{ activeCount: number; insights: AIInsight[] }> => {
    const response = await apiClient.get<{ activeCount: number; insights: AIInsight[] }>(
      `/namespaces/${namespaceId}/queues/${queueOrTopicName}/insights/summary`
    );
    return response.data;
  },
};
