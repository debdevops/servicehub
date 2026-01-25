import { apiClient } from './client';
import { AIInsight, GetInsightsParams } from './types';

// Feature flag to enable/disable insights API calls
// Set to false since the backend doesn't have InsightsController implemented yet
const INSIGHTS_ENABLED = false;

/**
 * Sanitize queue/topic name to remove $deadletterqueue suffix
 */
function sanitizeEntityName(name: string): string {
  return name.replace(/\/?\$deadletterqueue$/i, '');
}

export const insightsApi = {
  // GET /api/v1/namespaces/{namespaceId}/insights
  list: async (params: GetInsightsParams): Promise<AIInsight[]> => {
    // Return empty array if insights feature is disabled
    if (!INSIGHTS_ENABLED) {
      return [];
    }
    
    const { namespaceId, ...queryParams } = params;
    
    // Sanitize the queueOrTopicName if present
    if (queryParams.queueOrTopicName) {
      queryParams.queueOrTopicName = sanitizeEntityName(queryParams.queueOrTopicName);
    }
    
    const response = await apiClient.get<AIInsight[]>(
      `/namespaces/${namespaceId}/insights`,
      { params: queryParams }
    );
    
    return response.data;
  },

  // GET /api/v1/namespaces/{namespaceId}/insights/{insightId}
  get: async (namespaceId: string, insightId: string): Promise<AIInsight> => {
    // Return empty insight if disabled
    if (!INSIGHTS_ENABLED) {
      throw new Error('Insights feature is not enabled');
    }
    
    const response = await apiClient.get<AIInsight>(
      `/namespaces/${namespaceId}/insights/${insightId}`
    );
    return response.data;
  },

  // POST /api/v1/namespaces/{namespaceId}/insights/{insightId}/dismiss
  dismiss: async (namespaceId: string, insightId: string): Promise<void> => {
    if (!INSIGHTS_ENABLED) {
      return;
    }
    await apiClient.post(`/namespaces/${namespaceId}/insights/${insightId}/dismiss`);
  },

  // POST /api/v1/namespaces/{namespaceId}/insights/{insightId}/resolve
  resolve: async (namespaceId: string, insightId: string): Promise<void> => {
    if (!INSIGHTS_ENABLED) {
      return;
    }
    await apiClient.post(`/namespaces/${namespaceId}/insights/${insightId}/resolve`);
  },

  // GET /api/v1/namespaces/{namespaceId}/queues/{queueName}/insights/summary
  getSummary: async (namespaceId: string, queueOrTopicName: string): Promise<{ activeCount: number; insights: AIInsight[] }> => {
    // Return empty summary if disabled
    if (!INSIGHTS_ENABLED) {
      return { activeCount: 0, insights: [] };
    }
    
    // Sanitize queue/topic name
    const sanitizedName = sanitizeEntityName(queueOrTopicName);
    
    const response = await apiClient.get<{ activeCount: number; insights: AIInsight[] }>(
      `/namespaces/${namespaceId}/queues/${sanitizedName}/insights/summary`
    );
    return response.data;
  },
};
