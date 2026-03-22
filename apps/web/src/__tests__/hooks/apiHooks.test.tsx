import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import { useQueues } from '@/hooks/useQueues';
import { useTopics } from '@/hooks/useTopics';
import { useSubscriptions } from '@/hooks/useSubscriptions';
import { useNamespaces, useNamespace } from '@/hooks/useNamespaces';

// ─── Mock the API layer ────────────────────────────────────────────────────────

vi.mock('@/lib/api/client', () => ({
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

import { apiClient } from '@/lib/api/client';

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
    expect(apiClient.get).toHaveBeenCalledWith('/namespaces/ns-test/queues');
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
    expect(apiClient.get).toHaveBeenCalledWith('/namespaces/ns-xyz/topics');
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
});

// ─── useNamespaces ─────────────────────────────────────────────────────────────

vi.mock('@/lib/api/namespaces', () => ({
  namespacesApi: {
    list: vi.fn(),
    get: vi.fn(),
    create: vi.fn(),
    delete: vi.fn(),
    testConnection: vi.fn(),
  },
}));

import { namespacesApi } from '@/lib/api/namespaces';

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
