import { apiClient } from './client';
import { AIInsight, GetInsightsParams } from './types';

/**
 * AI Insights API
 * 
 * This module provides AI-powered pattern detection for Service Bus messages.
 * 
 * Current Implementation:
 * - Client-side analysis is always available
 * - Backend AI service is optional and may not be implemented
 * - Graceful degradation when backend is unavailable
 * 
 * TRUST GUARANTEES:
 * - All insights are labeled as "ServiceHub Interpretation"
 * - AI never presents inference as fact
 * - Uncertainty is explicitly stated
 * - Evidence (counts, IDs, time windows) always cited
 */

// Feature flag for backend AI service
// Set to false since the backend doesn't have InsightsController implemented yet
// When backend is available, set to true to use real API
const BACKEND_AI_ENABLED = false;

/**
 * Sanitize queue/topic name to remove $deadletterqueue suffix
 */
function sanitizeEntityName(name: string): string {
  return name.replace(/\/?\$deadletterqueue$/i, '');
}

export const insightsApi = {
  /**
   * List insights for a namespace/entity
   * 
   * When BACKEND_AI_ENABLED is false:
   * - Returns empty array (client-side analysis happens in hooks)
   * 
   * When BACKEND_AI_ENABLED is true:
   * - Calls backend API for AI insights
   */
  list: async (params: GetInsightsParams): Promise<AIInsight[]> => {
    // Return empty array if backend AI is disabled
    // Client-side analysis is handled by useInsights hook
    if (!BACKEND_AI_ENABLED) {
      return [];
    }
    
    const { namespaceId, ...queryParams } = params;
    
    // Sanitize the queueOrTopicName if present
    if (queryParams.queueOrTopicName) {
      queryParams.queueOrTopicName = sanitizeEntityName(queryParams.queueOrTopicName);
    }
    
    try {
      const response = await apiClient.get<AIInsight[]>(
        `/namespaces/${namespaceId}/insights`,
        { params: queryParams }
      );
      return response.data;
    } catch (error: any) {
      // Gracefully handle missing endpoint (404) or service unavailable (503)
      if (error?.response?.status === 404 || error?.response?.status === 503) {
        // Backend AI service not available, using client-side analysis
        return [];
      }
      throw error;
    }
  },

  /**
   * Get a specific insight by ID
   */
  get: async (namespaceId: string, insightId: string): Promise<AIInsight> => {
    if (!BACKEND_AI_ENABLED) {
      throw new Error('AI insights backend is not enabled. Client-side insights do not support individual lookup.');
    }
    
    const response = await apiClient.get<AIInsight>(
      `/namespaces/${namespaceId}/insights/${insightId}`
    );
    return response.data;
  },

  /**
   * Dismiss an insight
   */
  dismiss: async (namespaceId: string, insightId: string): Promise<void> => {
    if (!BACKEND_AI_ENABLED) {
      // For client-side insights, dismissal is not persistent
      return;
    }
    await apiClient.post(`/namespaces/${namespaceId}/insights/${insightId}/dismiss`);
  },

  /**
   * Resolve an insight
   */
  resolve: async (namespaceId: string, insightId: string): Promise<void> => {
    if (!BACKEND_AI_ENABLED) {
      // For client-side insights, resolution is not persistent
      return;
    }
    await apiClient.post(`/namespaces/${namespaceId}/insights/${insightId}/resolve`);
  },

  /**
   * Get insights summary for a queue/topic
   * 
   * When BACKEND_AI_ENABLED is false:
   * - Returns empty summary (client-side analysis happens in hooks)
   */
  getSummary: async (namespaceId: string, queueOrTopicName: string): Promise<{ activeCount: number; insights: AIInsight[] }> => {
    if (!BACKEND_AI_ENABLED) {
      // Return empty summary - actual analysis happens in hooks with real message data
      return { activeCount: 0, insights: [] };
    }
    
    const sanitizedName = sanitizeEntityName(queueOrTopicName);
    
    try {
      const response = await apiClient.get<{ activeCount: number; insights: AIInsight[] }>(
        `/namespaces/${namespaceId}/queues/${sanitizedName}/insights/summary`
      );
      return response.data;
    } catch (error: any) {
      if (error?.response?.status === 404 || error?.response?.status === 503) {
        return { activeCount: 0, insights: [] };
      }
      throw error;
    }
  },
  
  /**
   * Check if AI insights are available
   * This can be used to conditionally show AI UI elements
   */
  isAvailable: async (): Promise<boolean> => {
    // Client-side analysis is always available
    return true;
  },
};
