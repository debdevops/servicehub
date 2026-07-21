import { vi, describe, it, expect, beforeEach } from 'vitest';
import { fleetApi } from '@/lib/api/fleet';
import { apiClient } from '@/lib/api/client';

vi.mock('@/lib/api/client', () => ({
  apiClient: {
    get: vi.fn(),
    post: vi.fn(),
    defaults: { baseURL: 'http://localhost:5153/api/v1' },
  },
}));

const mocked = vi.mocked(apiClient, true);

describe('fleetApi', () => {
  beforeEach(() => vi.clearAllMocks());

  it('getOverview() calls GET /fleet/overview with default window', async () => {
    const overview = {
      generatedAt: '2026-07-21T00:00:00Z',
      windowHours: 24,
      namespaceCount: 2,
      totalActive: 5,
      totalNewInWindow: 1,
      totalResolvedInWindow: 0,
      namespaces: [],
      topCategories: {},
      dailyTrend: [],
    };
    mocked.get.mockResolvedValueOnce({ data: overview } as any);

    const result = await fleetApi.getOverview();

    expect(mocked.get).toHaveBeenCalledWith('/fleet/overview', { params: { windowHours: 24 } });
    expect(result.totalActive).toBe(5);
  });

  it('getOverview() forwards a custom window', async () => {
    mocked.get.mockResolvedValueOnce({ data: { windowHours: 168 } } as any);

    await fleetApi.getOverview(168);

    expect(mocked.get).toHaveBeenCalledWith('/fleet/overview', { params: { windowHours: 168 } });
  });
});
