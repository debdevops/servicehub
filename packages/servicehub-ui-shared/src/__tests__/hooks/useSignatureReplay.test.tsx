import { vi, describe, it, expect, beforeEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';

vi.mock('../../lib/api/signatureReplay', async () => {
  const actual = await vi.importActual<typeof import('../../lib/api/signatureReplay')>('../../lib/api/signatureReplay');
  return {
    ...actual,
    signatureReplayApi: {
      preview: vi.fn(),
      start: vi.fn(),
      getJob: vi.fn(),
      cancelJob: vi.fn(),
      history: vi.fn(),
    },
  };
});
vi.mock('react-hot-toast', () => ({
  default: { success: vi.fn(), error: vi.fn(), __call: vi.fn() },
}));

import { signatureReplayApi } from '../../lib/api/signatureReplay';
import type { BulkOperationJob } from '../../lib/api/bulkOperations';
import {
  useSignatureReplayPreview,
  useStartSignatureReplay,
  useSignatureReplayJob,
  useCancelSignatureReplayJob,
  useSignatureReplayHistory,
} from '../../hooks/useSignatureReplay';
import { DemoModeProvider } from '../../lib/demo/DemoContext';

const mockPreview = signatureReplayApi.preview as ReturnType<typeof vi.fn>;
const mockStart = signatureReplayApi.start as ReturnType<typeof vi.fn>;
const mockGetJob = signatureReplayApi.getJob as ReturnType<typeof vi.fn>;
const mockCancelJob = signatureReplayApi.cancelJob as ReturnType<typeof vi.fn>;
const mockHistory = signatureReplayApi.history as ReturnType<typeof vi.fn>;

function createWrapper() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return {
    Wrapper: ({ children }: { children: React.ReactNode }) =>
      React.createElement(QueryClientProvider, { client: queryClient }, children),
    queryClient,
  };
}

function createDemoWrapper(cloudProvider: 'azure' | 'aws' | 'gcp' = 'azure') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(
      QueryClientProvider,
      { client: queryClient },
      React.createElement(DemoModeProvider, { cloudProvider, children }),
    );
  };
}

function makeJob(overrides: Partial<BulkOperationJob> = {}): BulkOperationJob {
  return {
    id: 'job-1',
    operationType: 'Replay',
    status: 'Running',
    namespaceId: 'ns-1',
    namespaceDisplayName: 'ns-1',
    entityNameFilter: null,
    statusFilter: null,
    categoryFilter: null,
    from: null,
    to: null,
    totalMatched: 10,
    processedCount: 3,
    successCount: 3,
    failureCount: 0,
    skippedCount: 0,
    failureSample: null,
    errorSummary: null,
    createdAt: new Date().toISOString(),
    startedAt: new Date().toISOString(),
    completedAt: null,
    isCancellable: true,
    ...overrides,
  };
}

beforeEach(() => vi.clearAllMocks());

describe('useSignatureReplayPreview', () => {
  it('calls signatureReplayApi.preview with the given namespace/signature/filter', async () => {
    mockPreview.mockResolvedValue({ totalMatched: 5, sample: [], canExecute: true, warnings: [], unsafeReplayCount: 0 });
    const { Wrapper } = createWrapper();
    const { result } = renderHook(() => useSignatureReplayPreview(), { wrapper: Wrapper });

    result.current.mutate({ namespaceId: 'ns-1', signatureHash: 'hash-1', filter: { scope: 'all' } });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockPreview).toHaveBeenCalledWith('ns-1', 'hash-1', { scope: 'all' });
    expect(result.current.data?.totalMatched).toBe(5);
  });
});

describe('useStartSignatureReplay', () => {
  it('calls signatureReplayApi.start and returns the created job', async () => {
    const job = makeJob({ status: 'Pending' });
    mockStart.mockResolvedValue(job);
    const { Wrapper } = createWrapper();
    const { result } = renderHook(() => useStartSignatureReplay(), { wrapper: Wrapper });

    result.current.mutate({ namespaceId: 'ns-1', signatureHash: 'hash-1', filter: { scope: 'all' } });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(job);
  });
});

describe('useSignatureReplayJob', () => {
  it('is disabled when jobId is null', () => {
    const { Wrapper } = createWrapper();
    const { result } = renderHook(() => useSignatureReplayJob(null), { wrapper: Wrapper });

    expect(result.current.fetchStatus).toBe('idle');
    expect(mockGetJob).not.toHaveBeenCalled();
  });

  it('fetches the job and stops polling once terminal', async () => {
    mockGetJob.mockResolvedValue(makeJob({ status: 'Completed', completedAt: new Date().toISOString() }));
    const { Wrapper } = createWrapper();
    const { result } = renderHook(() => useSignatureReplayJob('job-1', 'ns-1', 'hash-1'), { wrapper: Wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.status).toBe('Completed');
  });

  it('invalidates the signature detail query on completion', async () => {
    mockGetJob.mockResolvedValue(makeJob({ status: 'Completed', completedAt: new Date().toISOString() }));
    const { Wrapper, queryClient } = createWrapper();
    queryClient.setQueryData(['dlq-signature-detail', 'ns-1', 'hash-1'], { stale: true });
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    renderHook(() => useSignatureReplayJob('job-1', 'ns-1', 'hash-1'), { wrapper: Wrapper });

    await waitFor(() =>
      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['dlq-signature-detail', 'ns-1', 'hash-1'] }),
    );
  });
});

describe('useCancelSignatureReplayJob', () => {
  it('calls signatureReplayApi.cancelJob and stores the returned job in the cache', async () => {
    const cancelledJob = makeJob({ status: 'Cancelled', isCancellable: false });
    mockCancelJob.mockResolvedValue(cancelledJob);
    const { Wrapper, queryClient } = createWrapper();
    const { result } = renderHook(() => useCancelSignatureReplayJob(), { wrapper: Wrapper });

    await act(async () => {
      await result.current.mutateAsync('job-1');
    });

    expect(mockCancelJob).toHaveBeenCalledWith('job-1');
    expect(queryClient.getQueryData(['signature-replay', 'job', 'job-1'])).toEqual(cancelledJob);
  });
});

describe('useSignatureReplayHistory', () => {
  it('is disabled when namespaceId or signatureHash is missing', () => {
    const { Wrapper } = createWrapper();
    const { result } = renderHook(() => useSignatureReplayHistory(undefined, 'hash-1'), { wrapper: Wrapper });

    expect(result.current.fetchStatus).toBe('idle');
    expect(mockHistory).not.toHaveBeenCalled();
  });

  it('calls signatureReplayApi.history with namespaceId, signatureHash, and default paging', async () => {
    const page = {
      items: [makeJob({ status: 'Completed' })],
      totalCount: 1,
      page: 1,
      pageSize: 20,
      hasNextPage: false,
      hasPreviousPage: false,
    };
    mockHistory.mockResolvedValue(page);
    const { Wrapper } = createWrapper();

    const { result } = renderHook(() => useSignatureReplayHistory('ns-1', 'hash-1'), { wrapper: Wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockHistory).toHaveBeenCalledWith('ns-1', 'hash-1', 1, 20);
    expect(result.current.data).toEqual(page);
  });

  it('returns curated demo replay history for a known demo hash without calling the real API', async () => {
    const { result } = renderHook(
      () => useSignatureReplayHistory('demo-azure-contoso-prod', 'demo-max-delivery-count-exceeded'),
      { wrapper: createDemoWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockHistory).not.toHaveBeenCalled();
    expect(result.current.data?.items.length).toBeGreaterThan(0);
    expect(result.current.data?.items[0].status).toBe('Completed');
  });

  it('returns an empty page for a demo signature with no replay history', async () => {
    const { result } = renderHook(
      () => useSignatureReplayHistory('demo-azure-contoso-prod', 'demo-deserialization-failure'),
      { wrapper: createDemoWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.items).toEqual([]);
  });
});
