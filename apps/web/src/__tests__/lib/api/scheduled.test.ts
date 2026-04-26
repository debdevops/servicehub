import { vi, describe, it, expect, beforeEach } from 'vitest';
import { scheduledApi } from '@/lib/api/scheduled';
import { apiClient } from '@/lib/api/client';

vi.mock('@/lib/api/client', () => ({
  apiClient: {
    get: vi.fn(),
    delete: vi.fn(),
  },
}));

const mockedClient = vi.mocked(apiClient, true);

const fakeMessage = {
  messageId: 'msg-1',
  sequenceNumber: 100,
  enqueuedTime: new Date().toISOString(),
  deliveryCount: 0,
  state: 'Scheduled' as const,
  contentType: 'application/json',
  body: '{"order":1}',
  scheduledEnqueueTime: new Date(Date.now() + 3_600_000).toISOString(),
};

const fakePaginatedResponse = {
  items: [fakeMessage],
  totalCount: 1,
  page: 1,
  pageSize: 100,
  hasNextPage: false,
  hasPreviousPage: false,
};

describe('scheduledApi', () => {
  beforeEach(() => vi.clearAllMocks());

  // ─── listScheduled() ───────────────────────────────────────────────────────

  describe('listScheduled()', () => {
    it('calls GET with correct URL including default skip/take', async () => {
      mockedClient.get.mockResolvedValueOnce({ data: fakePaginatedResponse } as any);

      const result = await scheduledApi.listScheduled('ns-1', 'my-queue');

      expect(mockedClient.get).toHaveBeenCalledWith(
        '/namespaces/ns-1/queues/my-queue/scheduled',
        { params: { skip: 0, take: 100 } }
      );
      expect(result.items).toHaveLength(1);
      expect(result.items[0].messageId).toBe('msg-1');
    });

    it('passes custom skip and take values', async () => {
      mockedClient.get.mockResolvedValueOnce({ data: { ...fakePaginatedResponse, items: [] } } as any);

      await scheduledApi.listScheduled('ns-2', 'orders', 50, 25);

      expect(mockedClient.get).toHaveBeenCalledWith(
        '/namespaces/ns-2/queues/orders/scheduled',
        { params: { skip: 50, take: 25 } }
      );
    });

    it('returns empty paginated response when no scheduled messages', async () => {
      const emptyResponse = { items: [], totalCount: 0, page: 1, pageSize: 100, hasNextPage: false, hasPreviousPage: false };
      mockedClient.get.mockResolvedValueOnce({ data: emptyResponse } as any);

      const result = await scheduledApi.listScheduled('ns-1', 'empty-queue');

      expect(result.items).toEqual([]);
      expect(result.totalCount).toBe(0);
    });

    it('propagates errors from the API client', async () => {
      mockedClient.get.mockRejectedValueOnce({ response: { status: 404 } });

      await expect(scheduledApi.listScheduled('ns-1', 'missing-queue')).rejects.toMatchObject({
        response: { status: 404 },
      });
    });
  });

  // ─── cancelScheduled() ────────────────────────────────────────────────────

  describe('cancelScheduled()', () => {
    it('calls DELETE with correct URL including sequence number', async () => {
      mockedClient.delete.mockResolvedValueOnce({ data: undefined } as any);

      await scheduledApi.cancelScheduled('ns-1', 'my-queue', 100);

      expect(mockedClient.delete).toHaveBeenCalledWith(
        '/namespaces/ns-1/queues/my-queue/scheduled/100',
        expect.objectContaining({
          headers: expect.objectContaining({
            'X-ServiceHub-Confirm': 'true',
            'X-ServiceHub-Intent': 'messages:cancel-scheduled',
          }),
        })
      );
    });

    it('propagates errors from the API client', async () => {
      mockedClient.delete.mockRejectedValueOnce({ response: { status: 403 } });

      await expect(scheduledApi.cancelScheduled('ns-1', 'my-queue', 100)).rejects.toMatchObject({
        response: { status: 403 },
      });
    });

    it('resolves without value on success', async () => {
      mockedClient.delete.mockResolvedValueOnce({ data: undefined } as any);

      const result = await scheduledApi.cancelScheduled('ns-1', 'my-queue', 42);

      expect(result).toBeUndefined();
    });
  });
});
