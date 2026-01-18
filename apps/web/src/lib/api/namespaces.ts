import { apiClient } from './client';
import { Namespace, CreateNamespaceRequest } from './types';

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
    await apiClient.delete(`/namespaces/${id}`);
  },

  // POST /api/v1/namespaces/{id}/test-connection
  testConnection: async (id: string): Promise<{ success: boolean; message: string }> => {
    const response = await apiClient.post<{ success: boolean; message: string }>(
      `/namespaces/${id}/test-connection`
    );
    return response.data;
  },
};
