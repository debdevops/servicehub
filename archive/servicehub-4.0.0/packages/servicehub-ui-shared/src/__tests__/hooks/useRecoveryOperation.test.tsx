import { vi, describe, it, expect, beforeEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';

vi.mock('react-hot-toast', () => ({
  default: { success: vi.fn(), error: vi.fn() },
}));

vi.mock('../../lib/api/recovery', () => ({
  recoveryApi: {
    getOperationById: vi.fn(),
    writeOff: vi.fn(),
    downloadExport: vi.fn(),
  },
}));

import { recoveryApi } from '../../lib/api/recovery';
import { useRecoveryOperation, useWriteOffRecoveryEntry, useDownloadRecoveryExport } from '../../hooks/useRecoveryOperation';
import { DemoModeProvider, DEMO_MODE_MUTATION_MESSAGE } from '../../lib/demo/DemoContext';

const mockGetOperationById = recoveryApi.getOperationById as ReturnType<typeof vi.fn>;
const mockWriteOff = recoveryApi.writeOff as ReturnType<typeof vi.fn>;
const mockDownloadExport = recoveryApi.downloadExport as ReturnType<typeof vi.fn>;

function createWrapper() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(QueryClientProvider, { client: queryClient }, children);
  };
}

function createDemoWrapper() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(
      QueryClientProvider,
      { client: queryClient },
      React.createElement(DemoModeProvider, { cloudProvider: 'azure' as const, children }),
    );
  };
}

describe('useRecoveryOperation', () => {
  beforeEach(() => vi.clearAllMocks());

  it('fetches the operation detail by id', async () => {
    mockGetOperationById.mockResolvedValueOnce({
      operation: { id: 'op-1' },
      entries: [],
      events: [],
    });

    const { result } = renderHook(() => useRecoveryOperation('op-1'), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockGetOperationById).toHaveBeenCalledWith('op-1');
  });

  it('does not fetch when operationId is null', () => {
    renderHook(() => useRecoveryOperation(null), { wrapper: createWrapper() });
    expect(mockGetOperationById).not.toHaveBeenCalled();
  });
});

describe('useWriteOffRecoveryEntry', () => {
  beforeEach(() => vi.clearAllMocks());

  it('calls the write-off endpoint with the entry id and reason', async () => {
    mockWriteOff.mockResolvedValueOnce({ id: 'entry-1', state: 'WrittenOff' });

    const { result } = renderHook(() => useWriteOffRecoveryEntry(), { wrapper: createWrapper() });

    await act(async () => {
      await result.current.mutateAsync({ entryId: 'entry-1', reason: 'unrecoverable' });
    });

    expect(mockWriteOff).toHaveBeenCalledWith('entry-1', 'unrecoverable');
  });

  it('rejects without calling the backend in Demo Mode', async () => {
    const { result } = renderHook(() => useWriteOffRecoveryEntry(), { wrapper: createDemoWrapper() });

    await expect(
      act(async () => {
        await result.current.mutateAsync({ entryId: 'entry-1', reason: 'unrecoverable' });
      }),
    ).rejects.toThrow(DEMO_MODE_MUTATION_MESSAGE);

    expect(mockWriteOff).not.toHaveBeenCalled();
  });
});

describe('useDownloadRecoveryExport', () => {
  beforeEach(() => vi.clearAllMocks());

  it('calls the real export endpoint outside Demo Mode', async () => {
    mockDownloadExport.mockResolvedValueOnce(undefined);

    const { result } = renderHook(() => useDownloadRecoveryExport(), { wrapper: createWrapper() });

    await act(async () => {
      await result.current.mutateAsync({ operationId: 'op-1', format: 'json' });
    });

    expect(mockDownloadExport).toHaveBeenCalledWith('op-1', 'json');
  });

  it('never calls the real export endpoint in Demo Mode, and builds a watermarked bundle instead', async () => {
    vi.spyOn(document.body, 'appendChild').mockImplementation((n) => n);
    vi.spyOn(document.body, 'removeChild').mockImplementation((n) => n);
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock-url');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
    const createElementSpy = vi.spyOn(document, 'createElement');

    const { result } = renderHook(() => useDownloadRecoveryExport(), { wrapper: createDemoWrapper() });

    await act(async () => {
      await result.current.mutateAsync({ operationId: 'op-1', format: 'json' });
    });

    expect(mockDownloadExport).not.toHaveBeenCalled();
    expect(createElementSpy).toHaveBeenCalledWith('a');
  });
});
