import { apiClient } from './client';
import type { CrossCloudTraceResponse } from './types';

export const crossCloudTraceApi = {
  /**
   * Traces a message by correlation/trace ID across all connected cloud namespaces.
   * Returns every hop found, sorted chronologically.
   */
  trace: async (traceId: string): Promise<CrossCloudTraceResponse> => {
    const response = await apiClient.get<CrossCloudTraceResponse>(
      '/cross-cloud-trace/trace',
      { params: { traceId } }
    );
    return response.data;
  },
};
