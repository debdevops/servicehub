import { vi, describe, it, expect, beforeEach } from 'vitest';
import { bulkOperationsApi, isTerminalBulkOperationStatus } from '@/lib/api/bulkOperations';
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

const filter = { namespaceId: 'ns-1', status: 'Active' };

describe('bulkOperationsApi', () => {
  beforeEach(() => vi.clearAllMocks());

  describe('preview()', () => {
    it('calls POST /bulk-operations/preview with the operation type and filter', async () => {
      const preview = { totalMatched: 3, sample: [], canExecute: true, warnings: [], unsafeReplayCount: 0 };
      mocked.post.mockResolvedValueOnce({ data: preview } as any);

      const result = await bulkOperationsApi.preview('Replay', filter);

      expect(mocked.post).toHaveBeenCalledWith('/bulk-operations/preview', {
        operationType: 'Replay',
        filter,
      });
      expect(result).toEqual(preview);
    });
  });

  describe('create()', () => {
    it('attaches the bulk:replay intent headers for a Replay operation', async () => {
      const job = { id: 'job-1', operationType: 'Replay', status: 'Pending' };
      mocked.post.mockResolvedValueOnce({ data: job } as any);

      await bulkOperationsApi.create('Replay', filter);

      expect(mocked.post).toHaveBeenCalledWith(
        '/bulk-operations',
        { operationType: 'Replay', filter },
        { headers: { 'X-ServiceHub-Intent': 'bulk:replay', 'X-ServiceHub-Confirm': 'true' } },
      );
    });

    it('attaches the bulk:purge intent headers for a Purge operation', async () => {
      const job = { id: 'job-2', operationType: 'Purge', status: 'Pending' };
      mocked.post.mockResolvedValueOnce({ data: job } as any);

      await bulkOperationsApi.create('Purge', filter);

      expect(mocked.post).toHaveBeenCalledWith(
        '/bulk-operations',
        { operationType: 'Purge', filter },
        { headers: { 'X-ServiceHub-Intent': 'bulk:purge', 'X-ServiceHub-Confirm': 'true' } },
      );
    });
  });

  describe('get()', () => {
    it('calls GET /bulk-operations/:id', async () => {
      const job = { id: 'job-1', status: 'Running' };
      mocked.get.mockResolvedValueOnce({ data: job } as any);

      const result = await bulkOperationsApi.get('job-1');

      expect(mocked.get).toHaveBeenCalledWith('/bulk-operations/job-1');
      expect(result).toEqual(job);
    });
  });

  describe('list()', () => {
    it('calls GET /bulk-operations with paging params', async () => {
      const page = { items: [], totalCount: 0, page: 1, pageSize: 20, hasNextPage: false, hasPreviousPage: false };
      mocked.get.mockResolvedValueOnce({ data: page } as any);

      await bulkOperationsApi.list('ns-1', 2, 10);

      expect(mocked.get).toHaveBeenCalledWith('/bulk-operations', {
        params: { namespaceId: 'ns-1', page: 2, pageSize: 10 },
      });
    });
  });

  describe('cancel()', () => {
    it('calls POST /bulk-operations/:id/cancel', async () => {
      const job = { id: 'job-1', status: 'Cancelled' };
      mocked.post.mockResolvedValueOnce({ data: job } as any);

      const result = await bulkOperationsApi.cancel('job-1');

      expect(mocked.post).toHaveBeenCalledWith('/bulk-operations/job-1/cancel');
      expect(result).toEqual(job);
    });
  });
});

describe('isTerminalBulkOperationStatus', () => {
  it.each(['Completed', 'CompletedWithErrors', 'Failed', 'Cancelled'] as const)(
    'returns true for %s',
    (status) => {
      expect(isTerminalBulkOperationStatus(status)).toBe(true);
    },
  );

  it.each(['Pending', 'Running'] as const)('returns false for %s', (status) => {
    expect(isTerminalBulkOperationStatus(status)).toBe(false);
  });
});
