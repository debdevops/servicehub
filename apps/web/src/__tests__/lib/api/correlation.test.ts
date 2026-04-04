import { vi, describe, it, expect, beforeEach } from 'vitest';
import { correlationApi } from '@/lib/api/correlation';
import { apiClient } from '@/lib/api/client';

vi.mock('@/lib/api/client', () => ({
  apiClient: {
    get: vi.fn(),
  },
}));

const mockedGet = vi.mocked(apiClient.get);

const fakeResponse = {
  correlationId: 'test-corr',
  entries: [
    {
      source: 'Live' as const,
      namespaceId: 'ns-1',
      namespaceDisplayName: 'My NS',
      entityName: 'my-queue',
      entityPath: 'my-queue',
      messageId: 'msg-1',
      sequenceNumber: 42,
      state: 'Active',
      timestamp: '2024-01-01T10:00:00Z',
      deadLetterReason: null,
      bodyPreview: '{"order":1}',
      sizeInBytes: 256,
    },
  ],
  totalCount: 1,
  namespacesSearched: 2,
  entitiesSearched: 5,
  isPartialResult: false,
  searchDurationMs: 123,
};

describe('correlationApi', () => {
  beforeEach(() => vi.clearAllMocks());

  describe('searchTimeline()', () => {
    it('calls GET /correlation/timeline with correlationId param', async () => {
      mockedGet.mockResolvedValueOnce({ data: fakeResponse } as any);

      await correlationApi.searchTimeline('test-corr');

      expect(mockedGet).toHaveBeenCalledWith(
        '/correlation/timeline',
        { params: { correlationId: 'test-corr' } }
      );
    });

    it('includes namespaceId param when provided', async () => {
      mockedGet.mockResolvedValueOnce({ data: fakeResponse } as any);

      await correlationApi.searchTimeline('test-corr', 'ns-abc');

      expect(mockedGet).toHaveBeenCalledWith(
        '/correlation/timeline',
        { params: { correlationId: 'test-corr', namespaceId: 'ns-abc' } }
      );
    });

    it('does not include namespaceId param when not provided', async () => {
      mockedGet.mockResolvedValueOnce({ data: fakeResponse } as any);

      await correlationApi.searchTimeline('test-corr');

      const callArgs = mockedGet.mock.calls[0][1] as any;
      expect(callArgs.params).not.toHaveProperty('namespaceId');
    });

    it('does not include namespaceId param when undefined', async () => {
      mockedGet.mockResolvedValueOnce({ data: fakeResponse } as any);

      await correlationApi.searchTimeline('test-corr', undefined);

      const callArgs = mockedGet.mock.calls[0][1] as any;
      expect(callArgs.params).not.toHaveProperty('namespaceId');
    });

    it('returns response data from the API', async () => {
      mockedGet.mockResolvedValueOnce({ data: fakeResponse } as any);

      const result = await correlationApi.searchTimeline('test-corr');

      expect(result).toEqual(fakeResponse);
      expect(result.correlationId).toBe('test-corr');
      expect(result.entries).toHaveLength(1);
      expect(result.entries[0].source).toBe('Live');
    });
  });
});
