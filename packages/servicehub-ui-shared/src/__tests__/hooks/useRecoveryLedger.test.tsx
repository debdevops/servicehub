import { vi, describe, it, expect, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';

vi.mock('../../lib/api/recovery', () => ({
  recoveryApi: {
    getOperations: vi.fn(),
    getEntries: vi.fn(),
    getApprovalQueue: vi.fn(),
  },
}));

import { recoveryApi } from '../../lib/api/recovery';
import { useRecoveryOperations, useRecoveryEntries, useApprovalQueue } from '../../hooks/useRecoveryLedger';

const mockGetOperations = recoveryApi.getOperations as ReturnType<typeof vi.fn>;
const mockGetEntries = recoveryApi.getEntries as ReturnType<typeof vi.fn>;
const mockGetApprovalQueue = recoveryApi.getApprovalQueue as ReturnType<typeof vi.fn>;

function createWrapper() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(QueryClientProvider, { client: queryClient }, children);
  };
}

describe('useRecoveryOperations', () => {
  beforeEach(() => vi.clearAllMocks());

  it('returns operations on success', async () => {
    mockGetOperations.mockResolvedValueOnce([{ id: 'op-1', kind: 'Replay' }]);

    const { result } = renderHook(() => useRecoveryOperations(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toHaveLength(1);
    expect(mockGetOperations).toHaveBeenCalledWith(undefined);
  });

  it('passes the namespace filter through', async () => {
    mockGetOperations.mockResolvedValueOnce([]);

    renderHook(() => useRecoveryOperations('ns-1'), { wrapper: createWrapper() });

    await waitFor(() => expect(mockGetOperations).toHaveBeenCalledWith('ns-1'));
  });
});

describe('useRecoveryEntries', () => {
  beforeEach(() => vi.clearAllMocks());

  it('forwards the dlqMessageId filter for the message-detail recovery record lookup', async () => {
    mockGetEntries.mockResolvedValueOnce([{ id: 'entry-1', dlqMessageId: 42 }]);

    const { result } = renderHook(() => useRecoveryEntries({ dlqMessageId: 42 }), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockGetEntries).toHaveBeenCalledWith({ dlqMessageId: 42 });
    expect(result.current.data?.[0].dlqMessageId).toBe(42);
  });
});

describe('useApprovalQueue', () => {
  beforeEach(() => vi.clearAllMocks());

  it('returns pending approvals on success', async () => {
    mockGetApprovalQueue.mockResolvedValueOnce([{ entryId: 'entry-1', ruleId: 1 }]);

    const { result } = renderHook(() => useApprovalQueue(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toHaveLength(1);
    expect(mockGetApprovalQueue).toHaveBeenCalledWith(undefined, 100);
  });

  it('passes the namespace filter through', async () => {
    mockGetApprovalQueue.mockResolvedValueOnce([]);

    renderHook(() => useApprovalQueue('ns-1'), { wrapper: createWrapper() });

    await waitFor(() => expect(mockGetApprovalQueue).toHaveBeenCalledWith('ns-1', 100));
  });
});
