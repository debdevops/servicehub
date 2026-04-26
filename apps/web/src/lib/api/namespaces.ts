import { apiClient } from './client';
import { riskIntent, withRiskIntent } from './intentHeaders';
import { Namespace, CreateNamespaceRequest } from './types';

export interface NamespaceStats {
  totalQueues: number;
  totalTopics: number;
  totalSubscriptions: number;
  totalActive: number;
  totalDlq: number;
  totalScheduled: number;
}

export const namespacesApi = {
  // GET /api/v1/namespaces
  list: async (): Promise<Namespace[]> => {
    const response = await apiClient.get<Namespace[]>('/namespaces');
    return response.data;
  },

  // POST /api/v1/namespaces
  create: async (data: CreateNamespaceRequest): Promise<Namespace> => {
    const response = await apiClient.post<Namespace>('/namespaces', data);
    return response.data;
  },

  // GET /api/v1/namespaces/{id}
  get: async (id: string): Promise<Namespace> => {
    const response = await apiClient.get<Namespace>(`/namespaces/${id}`);
    return response.data;
  },

  // DELETE /api/v1/namespaces/{id}
  delete: async (id: string): Promise<void> => {
    await apiClient.delete(`/namespaces/${id}`, {
      headers: withRiskIntent(riskIntent.deleteNamespace),
    });
  },

  // POST /api/v1/namespaces/{id}/test-connection
  testConnection: async (id: string): Promise<{ isConnected: boolean; message: string; testedAt: string }> => {
    const response = await apiClient.post<{ isConnected: boolean; message: string; testedAt: string }>(
      `/namespaces/${id}/test-connection`
    );
    return response.data;
  },

  // GET /api/v1/namespaces/{id}/stats
  getStats: async (id: string): Promise<NamespaceStats> => {
    const response = await apiClient.get<NamespaceStats>(`/namespaces/${id}/stats`, {
      _silent: true,
    } as Record<string, unknown>);
    return response.data;
  },
};
