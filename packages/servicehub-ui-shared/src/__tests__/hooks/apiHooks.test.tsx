import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import { useQueues, useAllNamespacesQueues } from '../../hooks/useQueues';
import { useTopics } from '../../hooks/useTopics';
import { useSubscriptions } from '../../hooks/useSubscriptions';
import { useNamespaces, useNamespace } from '../../hooks/useNamespaces';

// ─── Mock the API layer ────────────────────────────────────────────────────────

vi.mock('../../lib/api/client', () => ({
  apiClient: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}));

// Also mock react-hot-toast to avoid DOM noise
vi.mock('react-hot-toast', () => ({
  default: {
    success: vi.fn(),
    error: vi.fn(),
  },
  toast: { success: vi.fn(), error: vi.fn() },
}));

import { apiClient } from '../../lib/api/client';

// ─── Wrapper factory ───────────────────────────────────────────────────────────

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(QueryClientProvider, { client: queryClient }, children);
  };
}

// ─── useQueues ─────────────────────────────────────────────────────────────────

describe('useQueues', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('returns loading state initially', () => {
    (apiClient.get as ReturnType<typeof vi.fn>).mockReturnValue(new Promise(() => {}));
    const { result } = renderHook(() => useQueues('ns-001'), { wrapper: createWrapper() });
    expect(result.current.isLoading).toBe(true);
  });

  it('returns data after successful fetch', async () => {
    const mockQueues = [
      { name: 'orders-queue', activeMessageCount: 5, deadLetterMessageCount: 1 },
    ];
    (apiClient.get as ReturnType<typeof vi.fn>).mockResolvedValue({ data: mockQueues });

    const { result } = renderHook(() => useQueues('ns-001'), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(mockQueues);
  });

  it('returns error state when fetch fails', async () => {
    // Use 404 status so the hook's retry function short-circuits (no retries)
    (apiClient.get as ReturnType<typeof vi.fn>).mockRejectedValue({ response: { status: 404 } });

    const { result } = renderHook(() => useQueues('ns-001'), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.error).toBeDefined();
  });

  it('does NOT fetch when namespaceId is empty', () => {
    const { result } = renderHook(() => useQueues(''), { wrapper: createWrapper() });
    expect(result.current.isFetching).toBe(false);
    expect(apiClient.get).not.toHaveBeenCalled();
  });

  it('uses correct query endpoint', async () => {
    (apiClient.get as ReturnType<typeof vi.fn>).mockResolvedValue({ data: [] });
    renderHook(() => useQueues('ns-test'), { wrapper: createWrapper() });
    await waitFor(() => expect(apiClient.get).toHaveBeenCalled());
    expect(apiClient.get).toHaveBeenCalledWith('/namespaces/ns-test/queues', { _silent: true });
  });
});

// ─── useTopics ─────────────────────────────────────────────────────────────────

describe('useTopics', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('returns loading state initially', () => {
    (apiClient.get as ReturnType<typeof vi.fn>).mockReturnValue(new Promise(() => {}));
    const { result } = renderHook(() => useTopics('ns-001'), { wrapper: createWrapper() });
    expect(result.current.isLoading).toBe(true);
  });

  it('returns data after successful fetch', async () => {
    const mockTopics = [{ name: 'orders-topic', sizeInBytes: 1024, maxSizeInMegabytes: 1024, status: 'Active', subscriptionCount: 3 }];
    (apiClient.get as ReturnType<typeof vi.fn>).mockResolvedValue({ data: mockTopics });

    const { result } = renderHook(() => useTopics('ns-001'), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(mockTopics);
  });

  it('does NOT fetch when namespaceId is empty', () => {
    const { result } = renderHook(() => useTopics(''), { wrapper: createWrapper() });
    expect(result.current.isFetching).toBe(false);
  });

  it('calls the topics endpoint with the correct namespace', async () => {
    (apiClient.get as ReturnType<typeof vi.fn>).mockResolvedValue({ data: [] });
    renderHook(() => useTopics('ns-xyz'), { wrapper: createWrapper() });
    await waitFor(() => expect(apiClient.get).toHaveBeenCalled());
    expect(apiClient.get).toHaveBeenCalledWith('/namespaces/ns-xyz/topics', { _silent: true });
  });

  it('returns error state when fetch fails with 404', async () => {
    (apiClient.get as ReturnType<typeof vi.fn>).mockRejectedValue({ response: { status: 404 } });
    const { result } = renderHook(() => useTopics('ns-001'), { wrapper: createWrapper() });
    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});

// ─── useSubscriptions ──────────────────────────────────────────────────────────

describe('useSubscriptions', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('returns loading state initially', () => {
    (apiClient.get as ReturnType<typeof vi.fn>).mockReturnValue(new Promise(() => {}));
    const { result } = renderHook(
      () => useSubscriptions('ns-001', 'orders-topic'),
      { wrapper: createWrapper() }
    );
    expect(result.current.isLoading).toBe(true);
  });

  it('returns data after successful fetch', async () => {
    const mockSubs = [
      { name: 'sub-1', activeMessageCount: 2, deadLetterMessageCount: 0, topicName: 'orders-topic', status: 'Active' },
    ];
    (apiClient.get as ReturnType<typeof vi.fn>).mockResolvedValue({ data: mockSubs });

    const { result } = renderHook(
      () => useSubscriptions('ns-001', 'orders-topic'),
      { wrapper: createWrapper() }
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(mockSubs);
  });

  it('does NOT fetch when namespaceId is empty', () => {
    const { result } = renderHook(
      () => useSubscriptions('', 'orders-topic'),
      { wrapper: createWrapper() }
    );
    expect(result.current.isFetching).toBe(false);
  });

  it('does NOT fetch when topicName is empty', () => {
    const { result } = renderHook(
      () => useSubscriptions('ns-001', ''),
      { wrapper: createWrapper() }
    );
    expect(result.current.isFetching).toBe(false);
  });

  it('calls the subscriptions endpoint with correct namespace and topic', async () => {
    (apiClient.get as ReturnType<typeof vi.fn>).mockResolvedValue({ data: [] });
    renderHook(
      () => useSubscriptions('ns-abc', 'my-topic'),
      { wrapper: createWrapper() }
    );
    await waitFor(() => expect(apiClient.get).toHaveBeenCalled());
    expect(apiClient.get).toHaveBeenCalledWith(
      '/namespaces/ns-abc/topics/my-topic/subscriptions',
      { _silent: true }
    );
  });

  it('returns error state when fetch fails', async () => {
    (apiClient.get as ReturnType<typeof vi.fn>).mockRejectedValue({ response: { status: 500 } });
    const { result } = renderHook(
      () => useSubscriptions('ns-001', 'orders-topic'),
      { wrapper: createWrapper() }
    );
    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});

// ─── useNamespaces ─────────────────────────────────────────────────────────────

vi.mock('../../lib/api/namespaces', () => ({
  namespacesApi: {
    list: vi.fn(),
    get: vi.fn(),
    create: vi.fn(),
    delete: vi.fn(),
    testConnection: vi.fn(),
  },
}));

import { namespacesApi } from '../../lib/api/namespaces';

describe('useNamespaces', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('returns loading state initially', () => {
    (namespacesApi.list as ReturnType<typeof vi.fn>).mockReturnValue(new Promise(() => {}));
    const { result } = renderHook(() => useNamespaces(), { wrapper: createWrapper() });
    expect(result.current.isLoading).toBe(true);
  });

  it('returns namespace list after successful fetch', async () => {
    const mockNamespaces = [
      { id: 'ns-1', name: 'production-ns', isActive: true, createdAt: '2025-01-01' },
    ];
    (namespacesApi.list as ReturnType<typeof vi.fn>).mockResolvedValue(mockNamespaces);

    const { result } = renderHook(() => useNamespaces(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(mockNamespaces);
  });

  it('returns error state when list fetch fails', async () => {
    (namespacesApi.list as ReturnType<typeof vi.fn>).mockRejectedValue(new Error('fetch failed'));
    const { result } = renderHook(() => useNamespaces(), { wrapper: createWrapper() });
    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});

describe('useNamespace (single)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('fetches a single namespace by id', async () => {
    const mockNs = { id: 'ns-1', name: 'test-ns', isActive: true, createdAt: '2025-01-01' };
    (namespacesApi.get as ReturnType<typeof vi.fn>).mockResolvedValue(mockNs);

    const { result } = renderHook(() => useNamespace('ns-1'), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(mockNs);
  });

  it('does NOT fetch when id is empty', () => {
    const { result } = renderHook(() => useNamespace(''), { wrapper: createWrapper() });
    expect(result.current.isFetching).toBe(false);
  });
});
// ─── useAllNamespacesQueues ────────────────────────────────────────────────────

describe('useAllNamespacesQueues', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('returns empty array when namespaceIds is empty', () => {
    const { result } = renderHook(() => useAllNamespacesQueues([]), { wrapper: createWrapper() });
    expect(result.current).toEqual([]);
  });

  it('returns one entry per namespace with loading state initially, via a single batched POST', () => {
    (apiClient.post as ReturnType<typeof vi.fn>).mockReturnValue(new Promise(() => {}));
    const { result } = renderHook(
      () => useAllNamespacesQueues(['ns-1', 'ns-2']),
      { wrapper: createWrapper() },
    );
    expect(result.current).toHaveLength(2);
    expect(result.current[0].namespaceId).toBe('ns-1');
    expect(result.current[1].namespaceId).toBe('ns-2');
    expect(result.current[0].isLoading).toBe(true);
    // Fleet-scale fix: one POST /namespaces/stats/batch, not one GET per namespace.
    expect(apiClient.post).toHaveBeenCalledTimes(1);
    expect(apiClient.post).toHaveBeenCalledWith(
      '/namespaces/stats/batch',
      { namespaceIds: ['ns-1', 'ns-2'] },
      expect.objectContaining({ _silent: true }),
    );
  });

  it('aggregates queue stats after a successful batched fetch', async () => {
    (apiClient.post as ReturnType<typeof vi.fn>).mockResolvedValue({
      data: [
        {
          namespaceId: 'ns-1',
          stats: { totalQueues: 2, totalTopics: 0, totalSubscriptions: 0, totalActive: 8, totalDlq: 2, totalScheduled: 1 },
          queueNames: ['q1', 'q2'],
          topicNames: [],
        },
      ],
    });

    const { result } = renderHook(
      () => useAllNamespacesQueues(['ns-1']),
      { wrapper: createWrapper() },
    );

    await waitFor(() => expect(result.current[0].isLoading).toBe(false));
    expect(result.current[0].totalActive).toBe(8);
    expect(result.current[0].totalDlq).toBe(2);
    expect(result.current[0].totalScheduled).toBe(1);
    expect(result.current[0].totalQueues).toBe(2);
    expect(result.current[0].queues?.map((q) => q.name)).toEqual(['q1', 'q2']);
  });

  it('returns isError=true when the batch fetch fails', async () => {
    (apiClient.post as ReturnType<typeof vi.fn>).mockRejectedValue({ response: { status: 404 } });

    const { result } = renderHook(
      () => useAllNamespacesQueues(['ns-bad']),
      { wrapper: createWrapper() },
    );

    await waitFor(() => expect(result.current[0].isError).toBe(true));
    expect(result.current[0].totalActive).toBe(0);
    expect(result.current[0].queues).toBeUndefined();
  });

  it('shares one batched request across multiple consumers requesting the same namespace set', async () => {
    (apiClient.post as ReturnType<typeof vi.fn>).mockResolvedValue({
      data: [
        {
          namespaceId: 'ns-1',
          stats: { totalQueues: 1, totalTopics: 0, totalSubscriptions: 0, totalActive: 1, totalDlq: 0, totalScheduled: 0 },
          queueNames: ['q1'],
          topicNames: [],
        },
      ],
    });

    const wrapper = createWrapper();
    const first = renderHook(() => useAllNamespacesQueues(['ns-1']), { wrapper });
    renderHook(() => useAllNamespacesQueues(['ns-1']), { wrapper });

    await waitFor(() => expect(first.result.current[0].isLoading).toBe(false));
    // Two consumers, same namespace set — TanStack Query's cache dedupes them into one request,
    // the same property the old per-namespace query key relied on, now at batch granularity.
    expect(apiClient.post).toHaveBeenCalledTimes(1);
  });
});