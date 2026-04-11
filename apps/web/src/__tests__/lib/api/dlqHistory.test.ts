import { vi, describe, it, expect, beforeEach } from 'vitest';
import { dlqHistoryApi } from '@/lib/api/dlqHistory';
import { apiClient } from '@/lib/api/client';

vi.mock('@/lib/api/client', () => ({
  apiClient: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
    defaults: { baseURL: 'http://localhost:5153/api/v1' },
  },
}));

const mocked = vi.mocked(apiClient, true);

describe('dlqHistoryApi', () => {
  beforeEach(() => vi.clearAllMocks());

  describe('getHistory()', () => {
    it('calls GET /dlq/history with params', async () => {
      const mockPage = { items: [], totalCount: 0, page: 1, pageSize: 25, hasNextPage: false, hasPreviousPage: false };
      mocked.get.mockResolvedValueOnce({ data: mockPage } as any);

      const result = await dlqHistoryApi.getHistory({ namespaceId: 'ns-1' });

      expect(mocked.get).toHaveBeenCalledWith('/dlq/history', expect.objectContaining({ params: { namespaceId: 'ns-1' } }));
      expect(result.items).toEqual([]);
    });
  });

  describe('getById()', () => {
    it('calls GET /dlq/history/:id', async () => {
      const detail = { id: 1, messageId: 'msg-1', replayHistory: [] };
      mocked.get.mockResolvedValueOnce({ data: detail } as any);

      const result = await dlqHistoryApi.getById(1);

      expect(mocked.get).toHaveBeenCalledWith('/dlq/history/1');
      expect(result).toEqual(detail);
    });
  });

  describe('getTimeline()', () => {
    it('calls GET /dlq/history/:id/timeline', async () => {
      const timeline = { messageId: 1, entityName: 'my-queue', events: [] };
      mocked.get.mockResolvedValueOnce({ data: timeline } as any);

      const result = await dlqHistoryApi.getTimeline(1);

      expect(mocked.get).toHaveBeenCalledWith('/dlq/history/1/timeline');
      expect(result.events).toEqual([]);
    });
  });

  describe('updateNotes()', () => {
    it('calls POST /dlq/history/:id/notes', async () => {
      const updated = { id: 1, userNotes: 'my note' };
      mocked.post.mockResolvedValueOnce({ data: updated } as any);

      await dlqHistoryApi.updateNotes(1, 'my note');

      expect(mocked.post).toHaveBeenCalledWith('/dlq/history/1/notes', { notes: 'my note' });
    });
  });

  describe('getSummary()', () => {
    it('calls GET /dlq/summary without namespaceId', async () => {
      const summary = { totalMessages: 10, activeMessages: 5 };
      mocked.get.mockResolvedValueOnce({ data: summary } as any);

      const result = await dlqHistoryApi.getSummary();

      expect(mocked.get).toHaveBeenCalledWith('/dlq/summary', expect.objectContaining({ params: undefined }));
      expect(result).toEqual(summary);
    });

    it('calls GET /dlq/summary with namespaceId when provided', async () => {
      mocked.get.mockResolvedValueOnce({ data: {} } as any);

      await dlqHistoryApi.getSummary('ns-1');

      expect(mocked.get).toHaveBeenCalledWith('/dlq/summary', expect.objectContaining({ params: { namespaceId: 'ns-1' } }));
    });
  });

  describe('getExportUrl()', () => {
    it('builds export URL with json format by default', () => {
      const url = dlqHistoryApi.getExportUrl();
      expect(url).toContain('/dlq/export');
      expect(url).toContain('format=json');
    });

    it('builds export URL with csv format', () => {
      const url = dlqHistoryApi.getExportUrl('csv');
      expect(url).toContain('format=csv');
    });

    it('includes namespaceId in query params when provided', () => {
      const url = dlqHistoryApi.getExportUrl('json', { namespaceId: 'ns-1' });
      expect(url).toContain('namespaceId=ns-1');
    });

    it('includes entity filters in query params', () => {
      const url = dlqHistoryApi.getExportUrl('json', { entityName: 'my-queue', status: 'active' });
      expect(url).toContain('entityName=my-queue');
      expect(url).toContain('status=active');
    });
  });
});
