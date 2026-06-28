import { vi, describe, it, expect, beforeEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';

vi.mock('@/lib/api/messages', () => ({
  messagesApi: {
    list: vi.fn(),
    get: vi.fn(),
    send: vi.fn(),
    replay: vi.fn(),
    purge: vi.fn(),
  },
}));

vi.mock('react-hot-toast', () => ({
  default: { success: vi.fn(), error: vi.fn() },
}));

import { messagesApi } from '@/lib/api/messages';
import toast from 'react-hot-toast';
import {
  useMessages,
  useMessage,
  useSendMessage,
  useReplayMessage,
} from '@/hooks/useMessages';

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(QueryClientProvider, { client: queryClient }, children);
  };
}

// ─── useMessages ─────────────────────────────────────────────────────────────

describe('useMessages', () => {
  beforeEach(() => vi.clearAllMocks());

  it('fetches messages when params are provided', async () => {
    const mockMessages = { items: [{ id: 'msg-1', body: 'hello' }], totalCount: 1, hasMore: false };
    vi.mocked(messagesApi.list).mockResolvedValueOnce(mockMessages as any);

    const { result } = renderHook(
      () => useMessages({ namespaceId: 'ns-1', queueOrTopicName: 'my-queue' }),
      { wrapper: createWrapper() }
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(mockMessages);
    expect(messagesApi.list).toHaveBeenCalled();
  });

  it('is disabled when namespaceId is empty', () => {
    const { result } = renderHook(
      () => useMessages({ namespaceId: '', queueOrTopicName: 'my-queue' }),
      { wrapper: createWrapper() }
    );

    expect(result.current.fetchStatus).toBe('idle');
    expect(messagesApi.list).not.toHaveBeenCalled();
  });

  it('is disabled when queueOrTopicName is empty', () => {
    const { result } = renderHook(
      () => useMessages({ namespaceId: 'ns-1', queueOrTopicName: '' }),
      { wrapper: createWrapper() }
    );

    expect(result.current.fetchStatus).toBe('idle');
    expect(messagesApi.list).not.toHaveBeenCalled();
  });

  it('sanitizes $deadletterqueue suffix from queue name', async () => {
    const mockMessages = { items: [], totalCount: 0, hasMore: false };
    vi.mocked(messagesApi.list).mockResolvedValueOnce(mockMessages as any);

    renderHook(
      () => useMessages({ namespaceId: 'ns-1', queueOrTopicName: 'my-queue/$deadletterqueue' }),
      { wrapper: createWrapper() }
    );

    await waitFor(() => expect(messagesApi.list).toHaveBeenCalled());
    const callArgs = vi.mocked(messagesApi.list).mock.calls[0][0];
    expect(callArgs.queueOrTopicName).toBe('my-queue');
  });

  it('returns empty result on 404 error instead of throwing', async () => {
    vi.mocked(messagesApi.list).mockRejectedValueOnce({ response: { status: 404 } });

    const { result } = renderHook(
      () => useMessages({ namespaceId: 'ns-1', queueOrTopicName: 'my-queue' }),
      { wrapper: createWrapper() }
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual({
      items: [],
      totalCount: 0,
      page: 1,
      pageSize: 50,
      hasNextPage: false,
      hasPreviousPage: false,
    });
  });

  it('throws non-404 errors', async () => {
    vi.mocked(messagesApi.list).mockRejectedValue({ response: { status: 401 } });

    const { result } = renderHook(
      () => useMessages({ namespaceId: 'ns-1', queueOrTopicName: 'my-queue' }),
      { wrapper: createWrapper() }
    );

    // 401 errors do not retry (custom retry function returns false for 401)
    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});

// ─── useMessage (single) ─────────────────────────────────────────────────────

describe('useMessage', () => {
  beforeEach(() => vi.clearAllMocks());

  it('fetches single message by ID', async () => {
    const mockMsg = { id: 'msg-abc', body: '{"key":"value"}' };
    vi.mocked(messagesApi.get).mockResolvedValueOnce(mockMsg as any);

    const { result } = renderHook(
      () => useMessage('ns-1', 'msg-abc'),
      { wrapper: createWrapper() }
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(mockMsg);
  });

  it('is disabled when namespaceId is empty', () => {
    const { result } = renderHook(
      () => useMessage('', 'msg-abc'),
      { wrapper: createWrapper() }
    );

    expect(result.current.fetchStatus).toBe('idle');
    expect(messagesApi.get).not.toHaveBeenCalled();
  });

  it('is disabled when messageId is empty', () => {
    const { result } = renderHook(
      () => useMessage('ns-1', ''),
      { wrapper: createWrapper() }
    );

    expect(result.current.fetchStatus).toBe('idle');
    expect(messagesApi.get).not.toHaveBeenCalled();
  });
});

// ─── useSendMessage ───────────────────────────────────────────────────────────

describe('useSendMessage', () => {
  beforeEach(() => vi.clearAllMocks());

  it('sends message and shows success toast', async () => {
    vi.mocked(messagesApi.send).mockResolvedValueOnce(undefined as any);

    const { result } = renderHook(() => useSendMessage(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({
        namespaceId: 'ns-1',
        queueOrTopicName: 'my-queue',
        message: { body: 'test' },
        entityType: 'queue',
      });
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(toast.success).toHaveBeenCalledWith('Message sent successfully');
  });

  it('defaults to queue entityType', async () => {
    vi.mocked(messagesApi.send).mockResolvedValueOnce(undefined as any);

    const { result } = renderHook(() => useSendMessage(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({
        namespaceId: 'ns-1',
        queueOrTopicName: 'my-queue',
        message: { body: 'test' },
      });
    });

    await waitFor(() => expect(messagesApi.send).toHaveBeenCalledWith(
      'ns-1', 'my-queue', { body: 'test' }, 'queue'
    ));
  });

  it('shows error toast with API error message on failure', async () => {
    vi.mocked(messagesApi.send).mockRejectedValueOnce({
      response: { data: { message: 'Entity does not exist' } }
    });

    const { result } = renderHook(() => useSendMessage(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({
        namespaceId: 'ns-1',
        queueOrTopicName: 'bad-queue',
        message: { body: 'test' },
      });
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(toast.error).toHaveBeenCalledWith('Entity does not exist', expect.any(Object));
  });

  it('shows fallback error message when API provides none', async () => {
    vi.mocked(messagesApi.send).mockRejectedValueOnce(new Error('Network timeout'));

    const { result } = renderHook(() => useSendMessage(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({
        namespaceId: 'ns-1',
        queueOrTopicName: 'my-queue',
        message: { body: 'test' },
      });
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(toast.error).toHaveBeenCalledWith('Network timeout', expect.any(Object));
  });
});

// ─── useReplayMessage ─────────────────────────────────────────────────────────

describe('useReplayMessage', () => {
  beforeEach(() => vi.clearAllMocks());

  it('replays message and shows success toast', async () => {
    vi.mocked(messagesApi.replay).mockResolvedValueOnce(undefined as any);

    const { result } = renderHook(() => useReplayMessage(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({
        namespaceId: 'ns-1',
        sequenceNumber: 42,
        entityName: 'my-queue',
      });
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(toast.success).toHaveBeenCalledWith('Message replayed successfully');
    expect(messagesApi.replay).toHaveBeenCalledWith('ns-1', 42, 'my-queue', undefined);
  });

  it('shows 404-specific toast when replay is not available', async () => {
    vi.mocked(messagesApi.replay).mockRejectedValueOnce({ response: { status: 404 } });

    const { result } = renderHook(() => useReplayMessage(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({
        namespaceId: 'ns-1',
        sequenceNumber: 42,
        entityName: 'my-queue',
      });
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(toast.error).toHaveBeenCalledWith(
      expect.stringContaining('not yet available'),
      expect.any(Object)
    );
  });

  it('shows generic error toast for non-404 failures', async () => {
    vi.mocked(messagesApi.replay).mockRejectedValueOnce(new Error('Server error'));

    const { result } = renderHook(() => useReplayMessage(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({
        namespaceId: 'ns-1',
        sequenceNumber: 42,
        entityName: 'my-queue',
      });
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(toast.error).toHaveBeenCalledWith('Server error', expect.any(Object));
  });
});
