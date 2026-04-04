import { vi, describe, it, expect, beforeEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';

vi.mock('@/lib/api/scheduled', () => ({
  scheduledApi: {
    listScheduled: vi.fn(),
    cancelScheduled: vi.fn(),
  },
}));

vi.mock('react-hot-toast', () => ({
  default: { success: vi.fn(), error: vi.fn() },
}));

import { scheduledApi } from '@/lib/api/scheduled';
import toast from 'react-hot-toast';
import { useScheduledMessages, useCancelScheduledMessage } from '@/hooks/useScheduledMessages';

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(QueryClientProvider, { client: queryClient }, children);
  };
}

const fakeMessage = {
  messageId: 'msg-1',
  sequenceNumber: 100,
  enqueuedTime: new Date().toISOString(),
  deliveryCount: 0,
  state: 'Scheduled' as const,
  contentType: 'application/json',
  body: '{"order":1}',
  scheduledEnqueueTime: new Date(Date.now() + 3_600_000).toISOString(),
};

const fakePaginatedResponse = {
  items: [fakeMessage],
  totalCount: 1,
  page: 1,
  pageSize: 100,
  hasNextPage: false,
  hasPreviousPage: false,
};

// ─── useScheduledMessages ────────────────────────────────────────────────────

describe('useScheduledMessages', () => {
  beforeEach(() => vi.clearAllMocks());

  it('fetches scheduled messages when params are provided', async () => {
    vi.mocked(scheduledApi.listScheduled).mockResolvedValueOnce(fakePaginatedResponse as any);

    const { result } = renderHook(
      () => useScheduledMessages('ns-1', 'my-queue'),
      { wrapper: createWrapper() }
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.items).toHaveLength(1);
    expect(result.current.data?.items[0].messageId).toBe('msg-1');
    expect(scheduledApi.listScheduled).toHaveBeenCalledWith('ns-1', 'my-queue');
  });

  it('is disabled when namespaceId is empty', () => {
    const { result } = renderHook(
      () => useScheduledMessages('', 'my-queue'),
      { wrapper: createWrapper() }
    );

    expect(result.current.fetchStatus).toBe('idle');
    expect(scheduledApi.listScheduled).not.toHaveBeenCalled();
  });

  it('is disabled when queueName is empty', () => {
    const { result } = renderHook(
      () => useScheduledMessages('ns-1', ''),
      { wrapper: createWrapper() }
    );

    expect(result.current.fetchStatus).toBe('idle');
    expect(scheduledApi.listScheduled).not.toHaveBeenCalled();
  });

  it('returns error state when API call fails with a service error', async () => {
    vi.mocked(scheduledApi.listScheduled).mockRejectedValueOnce({ response: { status: 502 } });

    const { result } = renderHook(
      () => useScheduledMessages('ns-1', 'broken-queue'),
      { wrapper: createWrapper() }
    );

    await waitFor(() => expect(result.current.isError).toBe(true));
  });

  it('returns empty paginated response when queue has no scheduled messages', async () => {
    const emptyResponse = { items: [], totalCount: 0, page: 1, pageSize: 100, hasNextPage: false, hasPreviousPage: false };
    vi.mocked(scheduledApi.listScheduled).mockResolvedValueOnce(emptyResponse as any);

    const { result } = renderHook(
      () => useScheduledMessages('ns-1', 'empty-queue'),
      { wrapper: createWrapper() }
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.items).toEqual([]);
  });
});

// ─── useCancelScheduledMessage ───────────────────────────────────────────────

describe('useCancelScheduledMessage', () => {
  beforeEach(() => vi.clearAllMocks());

  it('calls scheduledApi.cancelScheduled with correct args and shows success toast', async () => {
    vi.mocked(scheduledApi.cancelScheduled).mockResolvedValueOnce(undefined);

    const { result } = renderHook(() => useCancelScheduledMessage(), {
      wrapper: createWrapper(),
    });

    await act(async () => {
      await result.current.mutateAsync({
        namespaceId: 'ns-1',
        queueName: 'my-queue',
        sequenceNumber: 100,
      });
    });

    expect(scheduledApi.cancelScheduled).toHaveBeenCalledWith('ns-1', 'my-queue', 100);
    expect(toast.success).toHaveBeenCalledWith('Scheduled message cancelled');
  });

  it('shows error toast when cancel fails with API error detail', async () => {
    vi.mocked(scheduledApi.cancelScheduled).mockRejectedValueOnce({
      response: { data: { detail: 'Message not found' } },
    });

    const { result } = renderHook(() => useCancelScheduledMessage(), {
      wrapper: createWrapper(),
    });

    await act(async () => {
      try {
        await result.current.mutateAsync({
          namespaceId: 'ns-1',
          queueName: 'my-queue',
          sequenceNumber: 999,
        });
      } catch {
        // expected
      }
    });

    expect(toast.error).toHaveBeenCalledWith('Message not found');
  });

  it('shows fallback error message when no API detail provided', async () => {
    vi.mocked(scheduledApi.cancelScheduled).mockRejectedValueOnce(new Error('Network error'));

    const { result } = renderHook(() => useCancelScheduledMessage(), {
      wrapper: createWrapper(),
    });

    await act(async () => {
      try {
        await result.current.mutateAsync({
          namespaceId: 'ns-1',
          queueName: 'my-queue',
          sequenceNumber: 100,
        });
      } catch {
        // expected
      }
    });

    expect(toast.error).toHaveBeenCalledWith('Failed to cancel scheduled message');
  });
});
