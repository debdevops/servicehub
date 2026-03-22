import { vi, describe, it, expect, beforeEach } from 'vitest';
import { insightsApi } from '@/lib/api/insights';
import { apiClient } from '@/lib/api/client';

vi.mock('@/lib/api/client', () => ({
  apiClient: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}));

const mocked = vi.mocked(apiClient);

describe('insightsApi', () => {
  beforeEach(() => vi.clearAllMocks());

  describe('list()', () => {
    it('returns empty array when BACKEND_AI_ENABLED is false (default)', async () => {
      // The module constant BACKEND_AI_ENABLED is false, so no API call is made
      const result = await insightsApi.list({ namespaceId: 'ns-1' });
      expect(result).toEqual([]);
      expect(mocked.get).not.toHaveBeenCalled();
    });
  });

  describe('get()', () => {
    it('throws when BACKEND_AI_ENABLED is false', async () => {
      await expect(insightsApi.get('ns-1', 'insight-1')).rejects.toThrow(
        'AI insights backend is not enabled'
      );
    });
  });

  describe('dismiss()', () => {
    it('resolves without error when BACKEND_AI_ENABLED is false', async () => {
      await expect(insightsApi.dismiss('ns-1', 'insight-1')).resolves.toBeUndefined();
      expect(mocked.post).not.toHaveBeenCalled();
    });
  });

  describe('resolve()', () => {
    it('resolves without error when BACKEND_AI_ENABLED is false', async () => {
      await expect(insightsApi.resolve('ns-1', 'insight-1')).resolves.toBeUndefined();
      expect(mocked.post).not.toHaveBeenCalled();
    });
  });

  describe('getSummary()', () => {
    it('returns empty summary when BACKEND_AI_ENABLED is false', async () => {
      const result = await insightsApi.getSummary('ns-1', 'my-queue');
      expect(result).toEqual({ activeCount: 0, insights: [] });
      expect(mocked.get).not.toHaveBeenCalled();
    });
  });

  describe('isAvailable()', () => {
    it('returns true (client-side analysis is always available)', async () => {
      const result = await insightsApi.isAvailable();
      expect(result).toBe(true);
    });
  });
});
