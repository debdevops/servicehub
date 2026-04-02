import { vi, describe, it, expect, beforeEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';

vi.mock('@/lib/api/correlation', () => ({
  correlationApi: {
    searchTimeline: vi.fn(),
  },
}));

vi.mock('react-hot-toast', () => ({
  default: { error: vi.fn(), success: vi.fn() },
}));

import { correlationApi } from '@/lib/api/correlation';
import toast from 'react-hot-toast';
import { useCorrelationSearch } from '@/hooks/useCorrelation';

const mockSearchTimeline = vi.mocked(correlationApi.searchTimeline);
const mockToastError = vi.mocked(toast.error);

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(QueryClientProvider, { client: queryClient }, children);
  };
}

const fakResponse = {
  correlationId: 'test-corr-id',
  entries: [],
  totalCount: 0,
  namespacesSearched: 1,
  entitiesSearched: 2,
  isPartialResult: false,
  searchDurationMs: 42,
};

describe('useCorrelationSearch', () => {
  beforeEach(() => vi.clearAllMocks());

  it('calls correlationApi.searchTimeline with correlationId', async () => {
    mockSearchTimeline.mockResolvedValueOnce(fakResponse);

    const { result } = renderHook(() => useCorrelationSearch(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({ correlationId: 'test-corr-id' });
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockSearchTimeline).toHaveBeenCalledWith('test-corr-id', undefined);
  });

  it('passes namespaceId when provided', async () => {
    mockSearchTimeline.mockResolvedValueOnce(fakResponse);

    const { result } = renderHook(() => useCorrelationSearch(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({ correlationId: 'corr-id', namespaceId: 'ns-123' });
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockSearchTimeline).toHaveBeenCalledWith('corr-id', 'ns-123');
  });

  it('does not pass namespaceId when omitted', async () => {
    mockSearchTimeline.mockResolvedValueOnce(fakResponse);

    const { result } = renderHook(() => useCorrelationSearch(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({ correlationId: 'corr-id' });
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockSearchTimeline).toHaveBeenCalledWith('corr-id', undefined);
  });

  it('returns response data on success', async () => {
    mockSearchTimeline.mockResolvedValueOnce(fakResponse);

    const { result } = renderHook(() => useCorrelationSearch(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({ correlationId: 'corr-id' });
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toEqual(fakResponse);
  });

  it('shows toast error on failure', async () => {
    mockSearchTimeline.mockRejectedValueOnce(
      Object.assign(new Error('Network error'), { response: undefined })
    );

    const { result } = renderHook(() => useCorrelationSearch(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({ correlationId: 'corr-id' });
    });

    await waitFor(() => expect(result.current.isError).toBe(true));

    expect(mockToastError).toHaveBeenCalledWith('Network error', { duration: 5000 });
  });
});
