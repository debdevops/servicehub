import { vi, describe, it, expect, beforeEach } from 'vitest';
import { renderHook, act, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';

vi.mock('@/lib/api/crossCloudTrace', () => ({
  crossCloudTraceApi: {
    trace: vi.fn(),
  },
}));

vi.mock('react-hot-toast', () => ({
  default: { error: vi.fn(), success: vi.fn() },
}));

import { crossCloudTraceApi } from '@/lib/api/crossCloudTrace';
import toast from 'react-hot-toast';
import { useCrossCloudTrace } from '@/hooks/useCrossCloudTrace';

const mockTrace = crossCloudTraceApi.trace as ReturnType<typeof vi.fn>;
const mockToastError = toast.error as ReturnType<typeof vi.fn>;

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(QueryClientProvider, { client: queryClient }, children);
  };
}

const mockTraceResult = {
  traceId: 'test-correlation-id',
  hops: [],
  namespaceSummaries: [],
  totalHops: 0,
  cloudsInvolved: 0,
  cloudProviders: [],
  isMultiCloud: false,
  namespacesSearched: 2,
  entitiesSearched: 5,
  isPartialResult: false,
  searchDurationMs: 100,
};

describe('useCrossCloudTrace', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('returns idle state initially', () => {
    const { result } = renderHook(() => useCrossCloudTrace(), { wrapper: createWrapper() });
    expect(result.current.isPending).toBe(false);
    expect(result.current.isSuccess).toBe(false);
    expect(result.current.data).toBeUndefined();
  });

  it('calls crossCloudTraceApi.trace with the provided traceId', async () => {
    mockTrace.mockResolvedValueOnce(mockTraceResult);
    const { result } = renderHook(() => useCrossCloudTrace(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate('my-trace-id');
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockTrace).toHaveBeenCalledWith('my-trace-id');
  });

  it('returns trace result data on success', async () => {
    mockTrace.mockResolvedValueOnce(mockTraceResult);
    const { result } = renderHook(() => useCrossCloudTrace(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate('test-correlation-id');
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(mockTraceResult);
  });

  it('calls toast.error with API error detail on failure', async () => {
    const apiError = {
      message: 'Network Error',
      response: { data: { detail: 'Trace search timeout' }, status: 504 },
    };
    mockTrace.mockRejectedValueOnce(apiError);
    const { result } = renderHook(() => useCrossCloudTrace(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate('bad-id');
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(mockToastError).toHaveBeenCalledWith('Trace search timeout', { duration: 6000 });
  });

  it('falls back to error.message when response detail is absent', async () => {
    const apiError = { message: 'Service unavailable', response: { data: {}, status: 503 } };
    mockTrace.mockRejectedValueOnce(apiError);
    const { result } = renderHook(() => useCrossCloudTrace(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate('bad-id');
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(mockToastError).toHaveBeenCalledWith('Service unavailable', { duration: 6000 });
  });

  it('shows default fallback message when error has no message', async () => {
    mockTrace.mockRejectedValueOnce({});
    const { result } = renderHook(() => useCrossCloudTrace(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate('empty-error');
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(mockToastError).toHaveBeenCalledWith('Cross-cloud trace failed.', { duration: 6000 });
  });
});
