import { vi, describe, it, expect, beforeEach } from 'vitest';
import {
  dlqSignaturesApi,
  type DlqSignaturesResponse,
  type DlqSignatureDetail,
  type SignatureTimelineResponse,
  type SignatureLifecycleStatusResponse,
} from '../../../lib/api/dlqSignatures';
import { apiClient } from '../../../lib/api/client';

vi.mock('../../../lib/api/client', () => ({
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
            signatureHash: 'hash-1',
            status: 'Active',
            trend: 'New',
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

  describe('getSignatureDetail()', () => {
    it('calls GET /namespaces/:namespaceId/dlq/signatures/:signatureHash', async () => {
      const response: DlqSignatureDetail = {
        signatureHash: 'hash-1',
        namespaceId: 'ns-1',
        size: 4,
        messageIds: [1, 2, 3, 4],
        dominantEntity: 'orders-queue',
        dominantDeadletterReason: 'MaxDeliveryCountExceeded',
        dominantDeadletterReasonCount: 4,
        topTerms: ['timeout'],
        isNew: false,
        firstSeenAt: '2026-01-01T00:00:00Z',
        occurrenceCount: 4,
        windowStart: '2026-01-01T00:00:00Z',
        windowEnd: '2026-01-01T01:00:00Z',
        explanation: '4 messages: max delivery count exceeded on orders-queue.',
        status: 'Active',
        trend: 'Recurring',
        confidence: 'High',
        isCurrentlyClustered: true,
        relatedMessages: [],
      };
      mocked.get.mockResolvedValueOnce({ data: response } as any);

      const result = await dlqSignaturesApi.getSignatureDetail('ns-1', 'hash-1');

      expect(mocked.get).toHaveBeenCalledWith('/namespaces/ns-1/dlq/signatures/hash-1');
      expect(result).toEqual(response);
    });
  });

  describe('getSignatureTimeline()', () => {
    it('calls GET /namespaces/:namespaceId/dlq/signatures/:signatureHash/timeline', async () => {
      const response: SignatureTimelineResponse = {
        signatureHash: 'hash-1',
        events: [
          { eventType: 'SignatureFirstObserved', description: 'first', timestamp: '2026-01-01T00:00:00Z', details: null },
        ],
      };
      mocked.get.mockResolvedValueOnce({ data: response } as any);

      const result = await dlqSignaturesApi.getSignatureTimeline('ns-1', 'hash-1');

      expect(mocked.get).toHaveBeenCalledWith('/namespaces/ns-1/dlq/signatures/hash-1/timeline');
      expect(result).toEqual(response);
    });
  });

  describe('updateSignatureStatus()', () => {
    it('calls POST /namespaces/:namespaceId/dlq/signatures/:signatureHash/status with status and notes', async () => {
      const response: SignatureLifecycleStatusResponse = {
        signatureHash: 'hash-1',
        status: 'Resolved',
        previousStatus: 'Active',
        transitionedAt: '2026-01-02T00:00:00Z',
        notes: 'fixed it',
      };
      mocked.post.mockResolvedValueOnce({ data: response } as any);

      const result = await dlqSignaturesApi.updateSignatureStatus('ns-1', 'hash-1', 'Resolved', 'fixed it');

      expect(mocked.post).toHaveBeenCalledWith('/namespaces/ns-1/dlq/signatures/hash-1/status', {
        status: 'Resolved',
        notes: 'fixed it',
      });
      expect(result).toEqual(response);
    });
  });
});
