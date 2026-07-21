import { vi, describe, it, expect, beforeEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';

vi.mock('@/lib/api/bulkOperations', async () => {
  const actual = await vi.importActual<typeof import('@/lib/api/bulkOperations')>('@/lib/api/bulkOperations');
  return {
    ...actual,
    bulkOperationsApi: {
      preview: vi.fn(),
      create: vi.fn(),
      get: vi.fn(),
      list: vi.fn(),
      cancel: vi.fn(),
    },
  };
});
vi.mock('react-hot-toast', () => ({
  default: { success: vi.fn(), error: vi.fn(), __call: vi.fn() },
}));

import { bulkOperationsApi, type BulkOperationJob } from '@/lib/api/bulkOperations';
import {
  useBulkOperationPreview,
  useCreateBulkOperation,
  useBulkOperationJob,
  useBulkOperationJobs,
  useCancelBulkOperation,
} from '@/hooks/useBulkOperations';

const mockPreview = bulkOperationsApi.preview as ReturnType<typeof vi.fn>;
const mockCreate = bulkOperationsApi.create as ReturnType<typeof vi.fn>;
const mockGet = bulkOperationsApi.get as ReturnType<typeof vi.fn>;
const mockList = bulkOperationsApi.list as ReturnType<typeof vi.fn>;
const mockCancel = bulkOperationsApi.cancel as ReturnType<typeof vi.fn>;

function createWrapper() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return {
    Wrapper: ({ children }: { children: React.ReactNode }) =>
      React.createElement(QueryClientProvider, { client: queryClient }, children),
    queryClient,
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
    statusFilter: 'Active',
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

describe('useBulkOperationPreview', () => {
  it('calls bulkOperationsApi.preview with the given operation/filter', async () => {
    mockPreview.mockResolvedValue({ totalMatched: 5, sample: [], canExecute: true, warnings: [], unsafeReplayCount: 0 });
    const { Wrapper } = createWrapper();
    const { result } = renderHook(() => useBulkOperationPreview(), { wrapper: Wrapper });

    result.current.mutate({ operationType: 'Replay', filter: { namespaceId: 'ns-1' } });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockPreview).toHaveBeenCalledWith('Replay', { namespaceId: 'ns-1' });
    expect(result.current.data?.totalMatched).toBe(5);
  });
});

describe('useCreateBulkOperation', () => {
  it('calls bulkOperationsApi.create and returns the created job', async () => {
    const job = makeJob({ status: 'Pending' });
    mockCreate.mockResolvedValue(job);
    const { Wrapper } = createWrapper();
    const { result } = renderHook(() => useCreateBulkOperation(), { wrapper: Wrapper });

    result.current.mutate({ operationType: 'Replay', filter: { namespaceId: 'ns-1' } });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(job);
  });
});

describe('useBulkOperationJob', () => {
  it('is disabled when jobId is null', () => {
    const { Wrapper } = createWrapper();
    const { result } = renderHook(() => useBulkOperationJob(null), { wrapper: Wrapper });

    expect(result.current.fetchStatus).toBe('idle');
    expect(mockGet).not.toHaveBeenCalled();
  });

  it('fetches the job and stops polling once terminal', async () => {
    mockGet.mockResolvedValue(makeJob({ status: 'Completed', completedAt: new Date().toISOString() }));
    const { Wrapper } = createWrapper();
    const { result } = renderHook(() => useBulkOperationJob('job-1'), { wrapper: Wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.status).toBe('Completed');
  });
});

describe('useBulkOperationJobs', () => {
  it('fetches the job list', async () => {
    mockList.mockResolvedValue({ items: [makeJob()], totalCount: 1, page: 1, pageSize: 20, hasNextPage: false, hasPreviousPage: false });
    const { Wrapper } = createWrapper();
    const { result } = renderHook(() => useBulkOperationJobs('ns-1'), { wrapper: Wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.totalCount).toBe(1);
    expect(mockList).toHaveBeenCalledWith('ns-1');
  });
});

describe('useCancelBulkOperation', () => {
  it('calls bulkOperationsApi.cancel and stores the returned job in the cache', async () => {
    const cancelledJob = makeJob({ status: 'Cancelled', isCancellable: false });
    mockCancel.mockResolvedValue(cancelledJob);
    const { Wrapper, queryClient } = createWrapper();
    const { result } = renderHook(() => useCancelBulkOperation(), { wrapper: Wrapper });

    await act(async () => {
      await result.current.mutateAsync('job-1');
    });

    expect(mockCancel).toHaveBeenCalledWith('job-1');
    expect(queryClient.getQueryData(['bulk-operations', 'job', 'job-1'])).toEqual(cancelledJob);
  });
});
