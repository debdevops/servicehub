import { vi, describe, it, expect, beforeEach } from 'vitest';
import { rulesApi } from '@/lib/api/rules';
import { apiClient } from '@/lib/api/client';

vi.mock('@/lib/api/client', () => ({
  apiClient: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}));

const mocked = vi.mocked(apiClient, true);

const mockRule = {
  id: 1,
  name: 'Test Rule',
  description: null,
  enabled: true,
  conditions: [],
  action: { autoReplay: true, delaySeconds: 0, maxRetries: 3, exponentialBackoff: false },
  createdAt: '2024-01-01T00:00:00Z',
  updatedAt: null,
  matchCount: 0,
  successCount: 0,
  successRate: 0,
  maxReplaysPerHour: 10,
  pendingMatchCount: 0,
};

describe('rulesApi', () => {
  beforeEach(() => vi.clearAllMocks());

  describe('getAll()', () => {
    it('calls GET /dlq/rules', async () => {
      mocked.get.mockResolvedValueOnce({ data: [mockRule] } as any);

      const result = await rulesApi.getAll();

      expect(mocked.get).toHaveBeenCalledWith('/dlq/rules', { params: undefined });
      expect(result).toHaveLength(1);
    });

    it('passes enabledOnly filter when provided', async () => {
      mocked.get.mockResolvedValueOnce({ data: [] } as any);

      await rulesApi.getAll(true);

      expect(mocked.get).toHaveBeenCalledWith('/dlq/rules', { params: { enabledOnly: true } });
    });
  });

  describe('getById()', () => {
    it('calls GET /dlq/rules/:id', async () => {
      mocked.get.mockResolvedValueOnce({ data: mockRule } as any);

      const result = await rulesApi.getById(1);

      expect(mocked.get).toHaveBeenCalledWith('/dlq/rules/1');
      expect(result.name).toBe('Test Rule');
    });
  });

  describe('create()', () => {
    it('calls POST /dlq/rules with request body', async () => {
      mocked.post.mockResolvedValueOnce({ data: mockRule } as any);
      const req = { name: 'New Rule', enabled: true, conditions: [], action: { autoReplay: true, delaySeconds: 0, maxRetries: 3, exponentialBackoff: false }, maxReplaysPerHour: 10 };

      const result = await rulesApi.create(req);

      expect(mocked.post).toHaveBeenCalledWith('/dlq/rules', req);
      expect(result.id).toBe(1);
    });
  });

  describe('update()', () => {
    it('calls PUT /dlq/rules/:id with request body', async () => {
      mocked.put.mockResolvedValueOnce({ data: mockRule } as any);
      const req = { name: 'Updated Rule', enabled: true, conditions: [], action: { autoReplay: false, delaySeconds: 5, maxRetries: 1, exponentialBackoff: true }, maxReplaysPerHour: 5 };

      await rulesApi.update(1, req);

      expect(mocked.put).toHaveBeenCalledWith('/dlq/rules/1', req);
    });
  });

  describe('delete()', () => {
    it('calls DELETE /dlq/rules/:id', async () => {
      mocked.delete.mockResolvedValueOnce({} as any);

      await rulesApi.delete(1);

      expect(mocked.delete).toHaveBeenCalledWith('/dlq/rules/1');
    });
  });

  describe('toggle()', () => {
    it('calls POST /dlq/rules/:id/toggle', async () => {
      mocked.post.mockResolvedValueOnce({ data: { ...mockRule, enabled: false } } as any);

      const result = await rulesApi.toggle(1);

      expect(mocked.post).toHaveBeenCalledWith('/dlq/rules/1/toggle');
      expect(result.enabled).toBe(false);
    });
  });

  describe('test()', () => {
    it('calls POST /dlq/rules/test with request body', async () => {
      const testResult = { totalTested: 5, matchedCount: 3, estimatedSuccessRate: 0.6, sampleMatches: [] };
      mocked.post.mockResolvedValueOnce({ data: testResult } as any);

      const result = await rulesApi.test({ ruleId: 1, namespaceId: 'ns-1' });

      expect(mocked.post).toHaveBeenCalledWith('/dlq/rules/test', { ruleId: 1, namespaceId: 'ns-1' });
      expect(result.matchedCount).toBe(3);
    });
  });

  describe('getTemplates()', () => {
    it('calls GET /dlq/rules/templates', async () => {
      const templates = [{ id: 'tmpl-1', name: 'Template 1', description: '', category: 'common', conditions: [], action: { autoReplay: true, delaySeconds: 0, maxRetries: 3, exponentialBackoff: false }, usageCount: 5, rating: 4.5 }];
      mocked.get.mockResolvedValueOnce({ data: templates } as any);

      const result = await rulesApi.getTemplates();

      expect(mocked.get).toHaveBeenCalledWith('/dlq/rules/templates');
      expect(result).toHaveLength(1);
    });
  });

  describe('replayAll()', () => {
    it('calls POST /dlq/rules/:id/replay-all with extended timeout', async () => {
      const replayResult = { totalMatched: 5, replayed: 4, failed: 1, skipped: 0, results: [] };
      mocked.post.mockResolvedValueOnce({ data: replayResult } as any);

      const result = await rulesApi.replayAll(1);

      expect(mocked.post).toHaveBeenCalledWith(
        '/dlq/rules/1/replay-all',
        null,
        expect.objectContaining({ timeout: 120_000 })
      );
      expect(result.replayed).toBe(4);
    });
  });
});
