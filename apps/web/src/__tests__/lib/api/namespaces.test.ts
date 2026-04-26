import { vi, describe, it, expect, beforeEach } from 'vitest';
import { namespacesApi } from '@/lib/api/namespaces';
import { apiClient } from '@/lib/api/client';

vi.mock('@/lib/api/client', () => ({
  apiClient: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}));

const mockedClient = vi.mocked(apiClient, true);

describe('namespacesApi', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('list()', () => {
    it('calls GET /namespaces', async () => {
      const mockData = [{ id: 'ns-1', name: 'Test NS' }];
      mockedClient.get.mockResolvedValueOnce({ data: mockData } as any);

      const result = await namespacesApi.list();

      expect(mockedClient.get).toHaveBeenCalledWith('/namespaces');
      expect(result).toEqual(mockData);
    });

    it('propagates errors from the API client', async () => {
      mockedClient.get.mockRejectedValueOnce(new Error('Network error'));
      await expect(namespacesApi.list()).rejects.toThrow('Network error');
    });
  });

  describe('create()', () => {
    it('calls POST /namespaces with the provided data', async () => {
      const payload = { name: 'New NS', connectionString: 'Endpoint=sb://...' };
      const created = { id: 'ns-2', ...payload };
      mockedClient.post.mockResolvedValueOnce({ data: created } as any);

      const result = await namespacesApi.create(payload as any);

      expect(mockedClient.post).toHaveBeenCalledWith('/namespaces', payload);
      expect(result).toEqual(created);
    });
  });

  describe('get()', () => {
    it('calls GET /namespaces/:id', async () => {
      const ns = { id: 'ns-1', name: 'Test' };
      mockedClient.get.mockResolvedValueOnce({ data: ns } as any);

      const result = await namespacesApi.get('ns-1');

      expect(mockedClient.get).toHaveBeenCalledWith('/namespaces/ns-1');
      expect(result).toEqual(ns);
    });
  });

  describe('delete()', () => {
    it('calls DELETE /namespaces/:id', async () => {
      mockedClient.delete.mockResolvedValueOnce({} as any);

      await namespacesApi.delete('ns-1');

      expect(mockedClient.delete).toHaveBeenCalledWith('/namespaces/ns-1');
    });
  });

  describe('testConnection()', () => {
    it('calls POST /namespaces/:id/test-connection', async () => {
      const result = { isConnected: true, message: 'OK', testedAt: '2024-01-01T00:00:00Z' };
      mockedClient.post.mockResolvedValueOnce({ data: result } as any);

      const response = await namespacesApi.testConnection('ns-1');

      expect(mockedClient.post).toHaveBeenCalledWith('/namespaces/ns-1/test-connection');
      expect(response.isConnected).toBe(true);
    });

    it('returns isConnected false when connection fails', async () => {
      const result = { isConnected: false, message: 'Connection refused', testedAt: '2024-01-01T00:00:00Z' };
      mockedClient.post.mockResolvedValueOnce({ data: result } as any);

      const response = await namespacesApi.testConnection('ns-1');

      expect(response.isConnected).toBe(false);
      expect(response.message).toBe('Connection refused');
    });
  });

  describe('getStats()', () => {
    it('calls GET /namespaces/:id/stats with _silent config', async () => {
      const mockStats = {
        totalQueues: 5,
        totalTopics: 3,
        totalSubscriptions: 8,
        totalActive: 150,
        totalDlq: 12,
        totalScheduled: 3,
      };
      mockedClient.get.mockResolvedValueOnce({ data: mockStats } as any);

      const result = await namespacesApi.getStats('ns-1');

      expect(mockedClient.get).toHaveBeenCalledWith('/namespaces/ns-1/stats', {
        _silent: true,
      });
      expect(result).toEqual(mockStats);
      expect(result.totalQueues).toBe(5);
      expect(result.totalDlq).toBe(12);
    });

    it('returns namespace statistics with correct shape', async () => {
      const mockStats = {
        totalQueues: 10,
        totalTopics: 4,
        totalSubscriptions: 12,
        totalActive: 500,
        totalDlq: 25,
        totalScheduled: 8,
      };
      mockedClient.get.mockResolvedValueOnce({ data: mockStats } as any);

      const result = await namespacesApi.getStats('ns-2');

      expect(result).toHaveProperty('totalQueues', 10);
      expect(result).toHaveProperty('totalTopics', 4);
      expect(result).toHaveProperty('totalSubscriptions', 12);
      expect(result).toHaveProperty('totalActive', 500);
      expect(result).toHaveProperty('totalDlq', 25);
      expect(result).toHaveProperty('totalScheduled', 8);
    });
  });
});
