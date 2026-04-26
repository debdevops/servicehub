import { vi, describe, it, expect, beforeEach } from 'vitest';
import { messagesApi } from '@/lib/api/messages';
import { apiClient } from '@/lib/api/client';

vi.mock('@/lib/api/client', () => ({
  apiClient: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}));

const mockedClient = vi.mocked(apiClient, true);

const fakeResponse = { items: [], totalCount: 0, hasMore: false };

describe('messagesApi', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('list()', () => {
    it('calls GET for queue messages with correct path', async () => {
      mockedClient.get.mockResolvedValueOnce({ data: fakeResponse } as any);

      await messagesApi.list({
        namespaceId: 'ns-1',
        queueOrTopicName: 'my-queue',
        entityType: 'queue',
      });

      expect(mockedClient.get).toHaveBeenCalledWith(
        '/namespaces/ns-1/queues/my-queue/messages',
        expect.any(Object)
      );
    });

    it('calls GET for topic messages with correct path', async () => {
      mockedClient.get.mockResolvedValueOnce({ data: fakeResponse } as any);

      await messagesApi.list({
        namespaceId: 'ns-1',
        queueOrTopicName: 'my-topic',
        entityType: 'topic',
      });

      expect(mockedClient.get).toHaveBeenCalledWith(
        '/namespaces/ns-1/topics/my-topic/messages',
        expect.any(Object)
      );
    });

    it('handles subscription path correctly (topic/subscriptions/sub)', async () => {
      mockedClient.get.mockResolvedValueOnce({ data: fakeResponse } as any);

      await messagesApi.list({
        namespaceId: 'ns-1',
        queueOrTopicName: 'my-topic/subscriptions/my-sub',
        entityType: 'topic',
      });

      expect(mockedClient.get).toHaveBeenCalledWith(
        '/namespaces/ns-1/topics/my-topic/subscriptions/my-sub/messages',
        expect.any(Object)
      );
    });

    it('sanitizes $deadletterqueue suffix from queue name', async () => {
      mockedClient.get.mockResolvedValueOnce({ data: fakeResponse } as any);

      await messagesApi.list({
        namespaceId: 'ns-1',
        queueOrTopicName: 'my-queue/$deadletterqueue',
        entityType: 'queue',
      });

      // Should strip the $deadletterqueue suffix
      expect(mockedClient.get).toHaveBeenCalledWith(
        '/namespaces/ns-1/queues/my-queue/messages',
        expect.any(Object)
      );
    });

    it('defaults to queue entity type when not specified', async () => {
      mockedClient.get.mockResolvedValueOnce({ data: fakeResponse } as any);

      await messagesApi.list({
        namespaceId: 'ns-1',
        queueOrTopicName: 'my-queue',
      } as any);

      expect(mockedClient.get).toHaveBeenCalledWith(
        '/namespaces/ns-1/queues/my-queue/messages',
        expect.any(Object)
      );
    });
  });

  describe('get()', () => {
    it('calls GET /namespaces/:id/messages/:messageId', async () => {
      const message = { id: 'msg-1', body: 'hello' };
      mockedClient.get.mockResolvedValueOnce({ data: message } as any);

      const result = await messagesApi.get('ns-1', 'msg-1');

      expect(mockedClient.get).toHaveBeenCalledWith('/namespaces/ns-1/messages/msg-1');
      expect(result).toEqual(message);
    });
  });

  describe('send()', () => {
    it('calls POST to queue messages endpoint', async () => {
      mockedClient.post.mockResolvedValueOnce({} as any);

      await messagesApi.send('ns-1', 'my-queue', { body: 'hello' });

      expect(mockedClient.post).toHaveBeenCalledWith(
        '/namespaces/ns-1/queues/my-queue/messages',
        expect.objectContaining({ body: 'hello' }),
        expect.objectContaining({
          headers: expect.objectContaining({
            'X-ServiceHub-Confirm': 'true',
            'X-ServiceHub-Intent': 'messages:send',
          }),
        })
      );
    });

    it('calls POST to topic messages endpoint when entityType is topic', async () => {
      mockedClient.post.mockResolvedValueOnce({} as any);

      await messagesApi.send('ns-1', 'my-topic', { body: 'hello' }, 'topic');

      expect(mockedClient.post).toHaveBeenCalledWith(
        '/namespaces/ns-1/topics/my-topic/messages',
        expect.objectContaining({ body: 'hello' }),
        expect.objectContaining({
          headers: expect.objectContaining({
            'X-ServiceHub-Confirm': 'true',
            'X-ServiceHub-Intent': 'messages:send',
          }),
        })
      );
    });

    it('maps properties to applicationProperties in payload', async () => {
      mockedClient.post.mockResolvedValueOnce({} as any);

      await messagesApi.send('ns-1', 'my-queue', {
        body: 'hello',
        properties: { key: 'value' },
      });

      expect(mockedClient.post).toHaveBeenCalledWith(
        '/namespaces/ns-1/queues/my-queue/messages',
        expect.objectContaining({ applicationProperties: { key: 'value' } }),
        expect.objectContaining({
          headers: expect.objectContaining({
            'X-ServiceHub-Confirm': 'true',
            'X-ServiceHub-Intent': 'messages:send',
          }),
        })
      );
    });
  });

  describe('replay()', () => {
    it('calls POST /messages/replay with query params', async () => {
      mockedClient.post.mockResolvedValueOnce({} as any);

      await messagesApi.replay('ns-1', 42, 'my-queue');

      expect(mockedClient.post).toHaveBeenCalledWith(
        '/messages/replay',
        null,
        expect.objectContaining({
          params: expect.objectContaining({
            namespaceId: 'ns-1',
            sequenceNumber: 42,
            entityName: 'my-queue',
          }),
        })
      );
    });

    it('passes subscriptionName when provided', async () => {
      mockedClient.post.mockResolvedValueOnce({} as any);

      await messagesApi.replay('ns-1', 42, 'my-topic', 'my-sub');

      expect(mockedClient.post).toHaveBeenCalledWith(
        '/messages/replay',
        null,
        expect.objectContaining({
          params: expect.objectContaining({ subscriptionName: 'my-sub' }),
        })
      );
    });
  });
});
