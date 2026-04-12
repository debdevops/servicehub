import { describe, it, expect, vi, beforeEach } from 'vitest';
import axios from 'axios';
import { ApiClient } from '@/lib/api/client';

// Mock axios
vi.mock('axios');

/**
 * Tests for API Client
 * Coverage target: 80%+ (currently 34.61%)
 * Importance: CRITICAL - All API communication
 */
describe('ApiClient', () => {
  let client: ApiClient;

  beforeEach(() => {
    vi.clearAllMocks();
    // Create new client for each test
    client = new ApiClient();
  });

  // ── Namespaces ────────────────────────────────────────────────────────────

  describe('Namespaces', () => {
    it('fetches namespaces', async () => {
      const mockNamespaces = [
        { id: '1', name: 'namespace-1', connectionString: 'conn1' },
        { id: '2', name: 'namespace-2', connectionString: 'conn2' },
      ];

      vi.mocked(axios.get).mockResolvedValueOnce({ data: mockNamespaces });

      const result = await client.getNamespaces();

      expect(result).toEqual(mockNamespaces);
      expect(axios.get).toHaveBeenCalledWith('/api/v1/namespaces');
    });

    it('handles namespace fetch error', async () => {
      const error = new Error('Failed to fetch namespaces');
      vi.mocked(axios.get).mockRejectedValueOnce(error);

      await expect(client.getNamespaces()).rejects.toThrow();
    });
  });

  // ── Queues ────────────────────────────────────────────────────────────────

  describe('Queues', () => {
    it('fetches queues for namespace', async () => {
      const mockQueues = [
        { name: 'queue1', messageCount: 10, dlqCount: 2 },
        { name: 'queue2', messageCount: 5, dlqCount: 0 },
      ];

      const namespaceId = 'ns-123';
      vi.mocked(axios.get).mockResolvedValueOnce({ data: mockQueues });

      const result = await client.getQueues(namespaceId);

      expect(result).toEqual(mockQueues);
      expect(axios.get).toHaveBeenCalledWith(`/api/v1/namespaces/${namespaceId}/queues`);
    });

    it('fetches single queue', async () => {
      const mockQueue = {
        name: 'queue1',
        messageCount: 10,
        dlqCount: 2,
        properties: {},
      };

      const namespaceId = 'ns-123';
      const queueName = 'queue1';
      vi.mocked(axios.get).mockResolvedValueOnce({ data: mockQueue });

      const result = await client.getQueue(namespaceId, queueName);

      expect(result).toEqual(mockQueue);
      expect(axios.get).toHaveBeenCalledWith(
        `/api/v1/namespaces/${namespaceId}/queues/${queueName}`
      );
    });
  });

  // ── Topics ────────────────────────────────────────────────────────────────

  describe('Topics', () => {
    it('fetches topics for namespace', async () => {
      const mockTopics = [
        { name: 'topic1', subscriptionCount: 3 },
        { name: 'topic2', subscriptionCount: 1 },
      ];

      const namespaceId = 'ns-123';
      vi.mocked(axios.get).mockResolvedValueOnce({ data: mockTopics });

      const result = await client.getTopics(namespaceId);

      expect(result).toEqual(mockTopics);
      expect(axios.get).toHaveBeenCalledWith(`/api/v1/namespaces/${namespaceId}/topics`);
    });
  });

  // ── Messages ──────────────────────────────────────────────────────────────

  describe('Messages', () => {
    it('fetches messages from queue', async () => {
      const mockMessages = [
        { messageId: 'msg1', body: 'test1', enqueuedAt: '2026-04-12T00:00:00Z' },
        { messageId: 'msg2', body: 'test2', enqueuedAt: '2026-04-12T00:01:00Z' },
      ];

      const namespaceId = 'ns-123';
      const queueName = 'queue1';
      vi.mocked(axios.get).mockResolvedValueOnce({ data: mockMessages });

      const result = await client.getMessages(namespaceId, queueName);

      expect(result).toEqual(mockMessages);
      expect(axios.get).toHaveBeenCalledWith(
        `/api/v1/namespaces/${namespaceId}/queues/${queueName}/messages`
      );
    });

    it('fetches dead-letter messages', async () => {
      const mockDLQMessages = [
        { messageId: 'dlq1', body: 'error1', deadLetterReason: 'Expired' },
        { messageId: 'dlq2', body: 'error2', deadLetterReason: 'MaxDelivery' },
      ];

      const namespaceId = 'ns-123';
      const queueName = 'queue1';
      vi.mocked(axios.get).mockResolvedValueOnce({ data: mockDLQMessages });

      const result = await client.getDeadLetterMessages(namespaceId, queueName);

      expect(result).toEqual(mockDLQMessages);
      expect(axios.get).toHaveBeenCalledWith(
        `/api/v1/namespaces/${namespaceId}/queues/${queueName}/messages/deadletter`
      );
    });

    it('sends a message', async () => {
      const namespaceId = 'ns-123';
      const queueName = 'queue1';
      const messageBody = 'test message';

      vi.mocked(axios.post).mockResolvedValueOnce({ data: { success: true } });

      const result = await client.sendMessage(namespaceId, queueName, messageBody);

      expect(result).toEqual({ success: true });
      expect(axios.post).toHaveBeenCalledWith(
        `/api/v1/namespaces/${namespaceId}/queues/${queueName}/messages`,
        expect.any(Object)
      );
    });
  });

  // ── DLQ History ───────────────────────────────────────────────────────────

  describe('DLQ History', () => {
    it('fetches DLQ history records', async () => {
      const mockHistory = [
        {
          id: 'hist1',
          queueName: 'queue1',
          reason: 'Expired',
          timestamp: '2026-04-12T00:00:00Z',
        },
        {
          id: 'hist2',
          queueName: 'queue2',
          reason: 'MaxDelivery',
          timestamp: '2026-04-12T00:01:00Z',
        },
      ];

      vi.mocked(axios.get).mockResolvedValueOnce({ data: mockHistory });

      const result = await client.getDLQHistory();

      expect(result).toEqual(mockHistory);
      expect(axios.get).toHaveBeenCalledWith('/api/v1/dlq-history');
    });

    it('scans DLQ for updates', async () => {
      const namespaceId = 'ns-123';
      vi.mocked(axios.post).mockResolvedValueOnce({ data: { scanned: 10, found: 3 } });

      const result = await client.scanDLQ(namespaceId);

      expect(result).toEqual({ scanned: 10, found: 3 });
      expect(axios.post).toHaveBeenCalledWith(
        `/api/v1/namespaces/${namespaceId}/dlq/scan`,
        expect.any(Object)
      );
    });
  });

  // ── Rules ─────────────────────────────────────────────────────────────────

  describe('Rules', () => {
    it('fetches replay rules', async () => {
      const mockRules = [
        {
          id: 'rule1',
          name: 'Transient Error Rule',
          condition: 'reason == "Transient"',
          enabled: true,
        },
      ];

      vi.mocked(axios.get).mockResolvedValueOnce({ data: mockRules });

      const result = await client.getRules();

      expect(result).toEqual(mockRules);
      expect(axios.get).toHaveBeenCalledWith('/api/v1/replay-rules');
    });

    it('creates a new rule', async () => {
      const newRule = {
        name: 'New Rule',
        condition: 'reason == "Timeout"',
        action: 'replay',
        enabled: true,
      };

      vi.mocked(axios.post).mockResolvedValueOnce({ data: { ...newRule, id: 'rule1' } });

      const result = await client.createRule(newRule);

      expect(result).toHaveProperty('id');
      expect(axios.post).toHaveBeenCalledWith('/api/v1/replay-rules', newRule);
    });

    it('updates a rule', async () => {
      const ruleId = 'rule1';
      const updates = { enabled: false };

      vi.mocked(axios.put).mockResolvedValueOnce({ data: { id: ruleId, ...updates } });

      const result = await client.updateRule(ruleId, updates);

      expect(result).toEqual({ id: ruleId, ...updates });
      expect(axios.put).toHaveBeenCalledWith(`/api/v1/replay-rules/${ruleId}`, updates);
    });

    it('deletes a rule', async () => {
      const ruleId = 'rule1';
      vi.mocked(axios.delete).mockResolvedValueOnce({ data: { success: true } });

      const result = await client.deleteRule(ruleId);

      expect(result).toEqual({ success: true });
      expect(axios.delete).toHaveBeenCalledWith(`/api/v1/replay-rules/${ruleId}`);
    });
  });

  // ── Health & Status ───────────────────────────────────────────────────────

  describe('Health & Status', () => {
    it('fetches system health', async () => {
      const mockHealth = {
        status: 'healthy',
        uptime: 3600,
        memory: { used: 512, available: 2048 },
      };

      vi.mocked(axios.get).mockResolvedValueOnce({ data: mockHealth });

      const result = await client.getHealth();

      expect(result).toEqual(mockHealth);
      expect(axios.get).toHaveBeenCalledWith('/api/v1/health');
    });
  });

  // ── Error Handling ────────────────────────────────────────────────────────

  describe('Error Handling', () => {
    it('handles network errors', async () => {
      const error = new Error('Network Error');
      vi.mocked(axios.get).mockRejectedValueOnce(error);

      await expect(client.getNamespaces()).rejects.toThrow('Network Error');
    });

    it('handles 404 errors', async () => {
      const error = {
        response: { status: 404, data: { message: 'Not found' } },
      };
      vi.mocked(axios.get).mockRejectedValueOnce(error);

      await expect(client.getNamespaces()).rejects.toThrow();
    });

    it('handles 500 server errors', async () => {
      const error = {
        response: { status: 500, data: { message: 'Internal Server Error' } },
      };
      vi.mocked(axios.get).mockRejectedValueOnce(error);

      await expect(client.getNamespaces()).rejects.toThrow();
    });
  });

  // ── Configuration ─────────────────────────────────────────────────────────

  describe('Configuration', () => {
    it('sets API base URL', () => {
      const baseURL = 'http://localhost:5153';
      client.setBaseURL(baseURL);

      expect(client.getBaseURL()).toBe(baseURL);
    });

    it('sets authorization header', () => {
      const token = 'test-token-123';
      client.setAuthToken(token);

      // Verify the header would be set (implementation dependent)
      expect(client.getAuthToken()).toBe(token);
    });
  });
});
