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

  describe('getTrend()', () => {
    it('calls GET /dlq/trend with namespaceId and days params', async () => {
      const mockTrend = [
        { date: '2026-04-20', newMessages: 5, resolvedMessages: 2 },
        { date: '2026-04-21', newMessages: 8, resolvedMessages: 3 },
      ];
      mocked.get.mockResolvedValueOnce({ data: mockTrend } as any);

      const result = await dlqHistoryApi.getTrend('ns-1', 7);

      expect(mocked.get).toHaveBeenCalledWith('/dlq/trend', {
        params: { namespaceId: 'ns-1', days: 7 },
      });
      expect(result).toEqual([
        { date: '2026-04-20', newCount: 5, resolvedCount: 2 },
        { date: '2026-04-21', newCount: 8, resolvedCount: 3 },
      ]);
    });

    it('maps response field names correctly (newMessages → newCount, resolvedMessages → resolvedCount)', async () => {
      const mockTrend = [{ date: '2026-04-22', newMessages: 10, resolvedMessages: 4 }];
      mocked.get.mockResolvedValueOnce({ data: mockTrend } as any);

      const result = await dlqHistoryApi.getTrend('ns-1');

      expect(result[0]).toHaveProperty('newCount', 10);
      expect(result[0]).toHaveProperty('resolvedCount', 4);
      expect(result[0]).not.toHaveProperty('newMessages');
      expect(result[0]).not.toHaveProperty('resolvedMessages');
    });

    it('uses default days=7 when not specified', async () => {
      mocked.get.mockResolvedValueOnce({ data: [] } as any);

      await dlqHistoryApi.getTrend('ns-1');

      expect(mocked.get).toHaveBeenCalledWith(
        '/dlq/trend',
        expect.objectContaining({ params: expect.objectContaining({ days: 7 }) })
      );
    });
  });

  describe('downloadExport()', () => {
    it('calls GET /dlq/export with format and params', async () => {
      const mockBlob = new Blob(['test data']);
      mocked.get.mockResolvedValueOnce({ data: mockBlob } as any);

      // Mock DOM methods
      const createElementSpy = vi.spyOn(document, 'createElement');
      const appendChildSpy = vi.spyOn(document.body, 'appendChild');
      const removeChildSpy = vi.spyOn(document.body, 'removeChild');

      await dlqHistoryApi.downloadExport('json', { namespaceId: 'ns-1' });

      expect(mocked.get).toHaveBeenCalledWith(
        expect.stringContaining('/dlq/export'),
        expect.objectContaining({ responseType: 'blob' })
      );
      expect(createElementSpy).toHaveBeenCalledWith('a');

      createElementSpy.mockRestore();
      appendChildSpy.mockRestore();
      removeChildSpy.mockRestore();
    });

    it('defers URL.revokeObjectURL cleanup with setTimeout', async () => {
      const mockBlob = new Blob(['test']);
      mocked.get.mockResolvedValueOnce({ data: mockBlob } as any);

      const setTimeoutSpy = vi.spyOn(global, 'setTimeout');
      vi.spyOn(document.body, 'appendChild').mockImplementation(() => document.body);
      vi.spyOn(document.body, 'removeChild').mockImplementation(() => document.body);
      vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock-url');
      vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});

      await dlqHistoryApi.downloadExport('csv');

      // Verify setTimeout was called to defer cleanup
      expect(setTimeoutSpy).toHaveBeenCalledWith(expect.any(Function), 0);

      setTimeoutSpy.mockRestore();
    });

    it('creates download link with correct filename', async () => {
      const mockBlob = new Blob(['test']);
      mocked.get.mockResolvedValueOnce({ data: mockBlob } as any);

      const createElementSpy = vi.spyOn(document, 'createElement');
      vi.spyOn(document.body, 'appendChild').mockImplementation(() => document.body);
      vi.spyOn(document.body, 'removeChild').mockImplementation(() => document.body);

      // Mock today's date
      vi.useFakeTimers();
      vi.setSystemTime(new Date('2026-04-22'));

      await dlqHistoryApi.downloadExport('json');

      expect(createElementSpy).toHaveBeenCalledWith('a');
      const anchor = createElementSpy.mock.results.find(r => r.value.tagName === 'A')?.value;
      expect(anchor?.download).toMatch(/dlq-export-2026-04-22\.json/);

      vi.useRealTimers();
      createElementSpy.mockRestore();
    });
  });
});
