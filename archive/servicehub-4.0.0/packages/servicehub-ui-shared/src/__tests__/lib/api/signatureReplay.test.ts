import { vi, describe, it, expect, beforeEach } from 'vitest';
import { signatureReplayApi } from '../../../lib/api/signatureReplay';
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

describe('signatureReplayApi', () => {
  beforeEach(() => vi.clearAllMocks());

  describe('preview()', () => {
    it('maps scope "all" to a filter with no status/date', async () => {
      const preview = { totalMatched: 3, sample: [], canExecute: true, warnings: [], unsafeReplayCount: 0 };
      mocked.post.mockResolvedValueOnce({ data: preview } as any);

      const result = await signatureReplayApi.preview('ns-1', 'hash-1', { scope: 'all' });

      expect(mocked.post).toHaveBeenCalledWith(
        '/namespaces/ns-1/dlq/signatures/hash-1/replay/preview',
        { status: null, from: null, to: null },
      );
      expect(result).toEqual(preview);
    });

    it('maps scope "unresolved" to status Active', async () => {
      mocked.post.mockResolvedValueOnce({ data: {} } as any);

      await signatureReplayApi.preview('ns-1', 'hash-1', { scope: 'unresolved' });

      expect(mocked.post).toHaveBeenCalledWith(
        '/namespaces/ns-1/dlq/signatures/hash-1/replay/preview',
        { status: 'Active', from: null, to: null },
      );
    });

    it('maps scope "failedReplay" to status ReplayFailed', async () => {
      mocked.post.mockResolvedValueOnce({ data: {} } as any);

      await signatureReplayApi.preview('ns-1', 'hash-1', { scope: 'failedReplay' });

      expect(mocked.post).toHaveBeenCalledWith(
        '/namespaces/ns-1/dlq/signatures/hash-1/replay/preview',
        { status: 'ReplayFailed', from: null, to: null },
      );
    });

    it('maps scope "dateRange" to from/to with no status', async () => {
      mocked.post.mockResolvedValueOnce({ data: {} } as any);

      await signatureReplayApi.preview('ns-1', 'hash-1', {
        scope: 'dateRange',
        from: '2026-01-01',
        to: '2026-01-31',
      });

      expect(mocked.post).toHaveBeenCalledWith(
        '/namespaces/ns-1/dlq/signatures/hash-1/replay/preview',
        { status: null, from: '2026-01-01', to: '2026-01-31' },
      );
    });
  });

  describe('start()', () => {
    it('attaches the signature:replay intent headers', async () => {
      const job = { id: 'job-1', status: 'Pending' };
      mocked.post.mockResolvedValueOnce({ data: job } as any);

      const result = await signatureReplayApi.start('ns-1', 'hash-1', { scope: 'all' });

      expect(mocked.post).toHaveBeenCalledWith(
        '/namespaces/ns-1/dlq/signatures/hash-1/replay',
        { status: null, from: null, to: null },
        { headers: { 'X-ServiceHub-Intent': 'signature:replay', 'X-ServiceHub-Confirm': 'true' } },
      );
      expect(result).toEqual(job);
    });
  });

  describe('getJob()', () => {
    it('calls GET /signature-replay-jobs/:id', async () => {
      const job = { id: 'job-1', status: 'Running' };
      mocked.get.mockResolvedValueOnce({ data: job } as any);

      const result = await signatureReplayApi.getJob('job-1');

      expect(mocked.get).toHaveBeenCalledWith('/signature-replay-jobs/job-1');
      expect(result).toEqual(job);
    });
  });

  describe('cancelJob()', () => {
    it('calls POST /signature-replay-jobs/:id/cancel', async () => {
      const job = { id: 'job-1', status: 'Cancelled' };
      mocked.post.mockResolvedValueOnce({ data: job } as any);

      const result = await signatureReplayApi.cancelJob('job-1');

      expect(mocked.post).toHaveBeenCalledWith('/signature-replay-jobs/job-1/cancel');
      expect(result).toEqual(job);
    });
  });

  describe('history()', () => {
    it('calls GET .../replay/history with default paging', async () => {
      const page = { items: [], totalCount: 0, page: 1, pageSize: 20, hasNextPage: false, hasPreviousPage: false };
      mocked.get.mockResolvedValueOnce({ data: page } as any);

      const result = await signatureReplayApi.history('ns-1', 'hash-1');

      expect(mocked.get).toHaveBeenCalledWith(
        '/namespaces/ns-1/dlq/signatures/hash-1/replay/history',
        { params: { page: 1, pageSize: 20 } },
      );
      expect(result).toEqual(page);
    });

    it('passes through explicit page/pageSize', async () => {
      mocked.get.mockResolvedValueOnce({ data: {} } as any);

      await signatureReplayApi.history('ns-1', 'hash-1', 2, 50);

      expect(mocked.get).toHaveBeenCalledWith(
        '/namespaces/ns-1/dlq/signatures/hash-1/replay/history',
        { params: { page: 2, pageSize: 50 } },
      );
    });
  });
});
