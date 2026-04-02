import { apiClient } from './client';
import { CorrelationTimelineResponse } from './types';

export const correlationApi = {
  // GET /api/v1/correlation/timeline?correlationId=X&namespaceId=Y
  searchTimeline: async (
    correlationId: string,
    namespaceId?: string
  ): Promise<CorrelationTimelineResponse> => {
    const params: Record<string, string> = { correlationId };
    if (namespaceId) params.namespaceId = namespaceId;
    const response = await apiClient.get<CorrelationTimelineResponse>(
      '/correlation/timeline',
      { params }
    );
    return response.data;
  },
};
