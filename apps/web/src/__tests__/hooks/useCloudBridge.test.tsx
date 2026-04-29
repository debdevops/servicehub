import { vi, describe, it, expect, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';

vi.mock('@/lib/api/cloudBridge', () => ({
  cloudBridgeApi: {
    getProviderStatus: vi.fn(),
    listEntities: vi.fn(),
    getVisibilityStatus: vi.fn(),
  },
}));

import { cloudBridgeApi } from '@/lib/api/cloudBridge';
import { useProviderStatus, useCloudEntities, useVisibilityStatus } from '@/hooks/useCloudBridge';

const mockGetProviderStatus = cloudBridgeApi.getProviderStatus as ReturnType<typeof vi.fn>;
const mockListEntities = cloudBridgeApi.listEntities as ReturnType<typeof vi.fn>;
const mockGetVisibilityStatus = cloudBridgeApi.getVisibilityStatus as ReturnType<typeof vi.fn>;

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(QueryClientProvider, { client: queryClient }, children);
  };
}

describe('useProviderStatus', () => {
  beforeEach(() => { vi.clearAllMocks(); });

  it('returns loading initially', () => {
    mockGetProviderStatus.mockReturnValue(new Promise(() => {}));
    const { result } = renderHook(() => useProviderStatus(), { wrapper: createWrapper() });
    expect(result.current.isLoading).toBe(true);
  });

  it('returns provider status map on success', async () => {
    const statusMap = { Aws: false, Gcp: false };
    mockGetProviderStatus.mockResolvedValue(statusMap);
    const { result } = renderHook(() => useProviderStatus(), { wrapper: createWrapper() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(statusMap);
  });

  it('returns error state when fetch fails', async () => {
    mockGetProviderStatus.mockRejectedValue(new Error('network error'));
    const { result } = renderHook(() => useProviderStatus(), { wrapper: createWrapper() });
    await waitFor(() => expect(result.current.isError).toBe(true), { timeout: 5000 });
    expect(result.current.error).toBeDefined();
  });
});

describe('useCloudEntities', () => {
  beforeEach(() => { vi.clearAllMocks(); });

  it('is disabled when namespaceId is null', () => {
    const { result } = renderHook(
      () => useCloudEntities({ namespaceId: null, provider: 'Aws' }),
      { wrapper: createWrapper() }
    );
    expect(result.current.fetchStatus).toBe('idle');
  });

  it('is disabled when provider is null', () => {
    const { result } = renderHook(
      () => useCloudEntities({ namespaceId: 'ns-1', provider: null }),
      { wrapper: createWrapper() }
    );
    expect(result.current.fetchStatus).toBe('idle');
  });

  it('fetches entities when both params are provided', async () => {
    const entities = [
      { name: 'my-queue', entityType: 'Queue', messageCount: 5, dlqCount: 0 },
    ];
    mockListEntities.mockResolvedValue(entities);
    const { result } = renderHook(
      () => useCloudEntities({ namespaceId: 'ns-1', provider: 'Aws' }),
      { wrapper: createWrapper() }
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(entities);
    expect(mockListEntities).toHaveBeenCalledWith('ns-1', 'Aws');
  });

  it('returns error state on fetch failure', async () => {
    mockListEntities.mockRejectedValue(new Error('api error'));
    const { result } = renderHook(
      () => useCloudEntities({ namespaceId: 'ns-1', provider: 'Aws' }),
      { wrapper: createWrapper() }
    );
    await waitFor(() => expect(result.current.isError).toBe(true), { timeout: 5000 });
  });
});

describe('useVisibilityStatus', () => {
  beforeEach(() => { vi.clearAllMocks(); });

  it('is disabled when any param is null', () => {
    const { result } = renderHook(
      () => useVisibilityStatus({ namespaceId: null, queueName: 'q1', provider: 'Aws' }),
      { wrapper: createWrapper() }
    );
    expect(result.current.fetchStatus).toBe('idle');
  });

  it('fetches visibility status when all params provided', async () => {
    const visStatus = { provider: 'Aws', isAvailable: true, details: {} };
    mockGetVisibilityStatus.mockResolvedValue(visStatus);
    const { result } = renderHook(
      () => useVisibilityStatus({ namespaceId: 'ns-1', queueName: 'my-queue', provider: 'Aws' }),
      { wrapper: createWrapper() }
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(visStatus);
    expect(mockGetVisibilityStatus).toHaveBeenCalledWith('ns-1', 'my-queue', 'Aws');
  });
});
