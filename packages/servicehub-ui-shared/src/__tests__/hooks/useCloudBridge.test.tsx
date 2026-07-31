import { vi, describe, it, expect, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';

vi.mock('../../lib/api/cloudBridge', () => ({
  cloudBridgeApi: {
    getProviderStatus: vi.fn(),
    getCapabilities: vi.fn(),
    listEntities: vi.fn(),
    getVisibilityStatus: vi.fn(),
  },
}));

import { cloudBridgeApi } from '../../lib/api/cloudBridge';
import { useProviderStatus, useProviderCapabilities, useCloudEntities, useVisibilityStatus } from '../../hooks/useCloudBridge';
import { DemoModeProvider } from '../../lib/demo/DemoContext';

const mockGetProviderStatus = cloudBridgeApi.getProviderStatus as ReturnType<typeof vi.fn>;
const mockGetCapabilities = cloudBridgeApi.getCapabilities as ReturnType<typeof vi.fn>;
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

function createDemoWrapper(cloudProvider: 'azure' | 'aws' | 'gcp') {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(
      QueryClientProvider,
      { client: queryClient },
      React.createElement(DemoModeProvider, { cloudProvider, children })
    );
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

describe('useProviderCapabilities', () => {
  beforeEach(() => { vi.clearAllMocks(); });

  it('returns loading initially', () => {
    mockGetCapabilities.mockReturnValue(new Promise(() => {}));
    const { result } = renderHook(() => useProviderCapabilities(), { wrapper: createWrapper() });
    expect(result.current.isLoading).toBe(true);
  });

  it('returns the capabilities map on success', async () => {
    const capabilitiesMap = {
      Azure: { supportsMessageCounts: true, supportsManualDeadLetter: true, supportsPurge: false, supportsScheduledMessages: true, supportsRepeatablePeek: true, notes: '' },
      Aws: { supportsMessageCounts: true, supportsManualDeadLetter: true, supportsPurge: true, supportsScheduledMessages: false, supportsRepeatablePeek: false, notes: '' },
      Gcp: { supportsMessageCounts: false, supportsManualDeadLetter: false, supportsPurge: true, supportsScheduledMessages: false, supportsRepeatablePeek: true, notes: '' },
    };
    mockGetCapabilities.mockResolvedValue(capabilitiesMap);
    const { result } = renderHook(() => useProviderCapabilities(), { wrapper: createWrapper() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(capabilitiesMap);
  });
});

describe('useProviderStatus in Demo Mode', () => {
  beforeEach(() => { vi.clearAllMocks(); });

  it('does not call the real API', async () => {
    const { result } = renderHook(() => useProviderStatus(), { wrapper: createDemoWrapper('azure') });
    expect(result.current.fetchStatus).toBe('idle');
    expect(mockGetProviderStatus).not.toHaveBeenCalled();
  });
});

describe('useProviderCapabilities in Demo Mode', () => {
  beforeEach(() => { vi.clearAllMocks(); });

  it('returns the AWS mock preset without calling the real API', async () => {
    const { result } = renderHook(() => useProviderCapabilities(), { wrapper: createDemoWrapper('aws') });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data?.Aws.supportsRepeatablePeek).toBe(false);
    expect(result.current.data?.Aws.supportsScheduledMessages).toBe(false);
    expect(mockGetCapabilities).not.toHaveBeenCalled();
  });

  it('returns the GCP mock preset without calling the real API', async () => {
    const { result } = renderHook(() => useProviderCapabilities(), { wrapper: createDemoWrapper('gcp') });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data?.Gcp.supportsMessageCounts).toBe(false);
    expect(result.current.data?.Gcp.supportsManualDeadLetter).toBe(false);
    expect(result.current.data?.Gcp.supportsScheduledMessages).toBe(false);
    expect(mockGetCapabilities).not.toHaveBeenCalled();
  });

  it('returns the Azure mock preset (unchanged) without calling the real API', async () => {
    const { result } = renderHook(() => useProviderCapabilities(), { wrapper: createDemoWrapper('azure') });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data?.Azure.supportsRepeatablePeek).toBe(true);
    expect(result.current.data?.Azure.supportsScheduledMessages).toBe(true);
    expect(mockGetCapabilities).not.toHaveBeenCalled();
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

describe('useCloudEntities in Demo Mode', () => {
  beforeEach(() => { vi.clearAllMocks(); });

  it('does not call the real API even with valid params', async () => {
    const { result } = renderHook(
      () => useCloudEntities({ namespaceId: 'demo-azure-contoso-prod', provider: 'Azure' }),
      { wrapper: createDemoWrapper('azure') }
    );
    expect(result.current.fetchStatus).toBe('idle');
    expect(mockListEntities).not.toHaveBeenCalled();
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

describe('useVisibilityStatus in Demo Mode', () => {
  beforeEach(() => { vi.clearAllMocks(); });

  it('does not call the real API even with valid params', async () => {
    const { result } = renderHook(
      () => useVisibilityStatus({ namespaceId: 'demo-azure-contoso-prod', queueName: 'orders-queue', provider: 'Azure' }),
      { wrapper: createDemoWrapper('azure') }
    );
    expect(result.current.fetchStatus).toBe('idle');
    expect(mockGetVisibilityStatus).not.toHaveBeenCalled();
  });
});
