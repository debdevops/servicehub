import { vi, describe, it, expect, beforeEach } from 'vitest';
import { dlqSignaturesApi, type DlqSignaturesResponse } from '@/lib/api/dlqSignatures';
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

describe('dlqSignaturesApi', () => {
  beforeEach(() => vi.clearAllMocks());

  describe('getSignatures()', () => {
    it('calls GET /namespaces/:namespaceId/dlq/signatures', async () => {
      const response: DlqSignaturesResponse = {
        available: true,
        method: 'clustered',
        batchSize: 5,
        clusters: [
          {
            size: 4,
            messageIds: [1, 2, 3, 4],
            dominantEntity: 'orders-queue',
            dominantDeadletterReason: 'MaxDeliveryCountExceeded',
            dominantDeadletterReasonCount: 4,
            topTerms: ['timeout'],
            isNew: true,
            firstSeenAt: '2026-01-01T00:00:00Z',
            occurrenceCount: 1,
            windowStart: '2026-01-01T00:00:00Z',
            windowEnd: '2026-01-01T01:00:00Z',
            explanation: '4 messages: max delivery count exceeded on orders-queue.',
          },
        ],
        singletons: [{ messageId: 5, dominantEntity: 'orders-queue', dominantDeadletterReason: 'TTLExpiredException' }],
      };
      mocked.get.mockResolvedValueOnce({ data: response } as any);

      const result = await dlqSignaturesApi.getSignatures('ns-1');

      expect(mocked.get).toHaveBeenCalledWith('/namespaces/ns-1/dlq/signatures');
      expect(result).toEqual(response);
    });

    it('returns an available:false body without throwing', async () => {
      const response: DlqSignaturesResponse = {
        available: false,
        method: null,
        batchSize: 5,
        clusters: [],
        singletons: [],
      };
      mocked.get.mockResolvedValueOnce({ data: response } as any);

      const result = await dlqSignaturesApi.getSignatures('ns-1');

      expect(result.available).toBe(false);
      expect(result.clusters).toEqual([]);
    });
  });
});
