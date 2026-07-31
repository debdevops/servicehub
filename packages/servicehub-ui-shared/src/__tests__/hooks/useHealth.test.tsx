import { vi, describe, it, expect, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';

vi.mock('../../lib/api/health', () => ({
  healthApi: {
    getVersion: vi.fn(),
    getStatus: vi.fn(),
    getReport: vi.fn(),
  },
}));

import { healthApi } from '../../lib/api/health';
import { useHealthVersion, useHealthStatus, useHealthReport } from '../../hooks/useHealth';

const mockGetVersion = healthApi.getVersion as ReturnType<typeof vi.fn>;
const mockGetStatus = healthApi.getStatus as ReturnType<typeof vi.fn>;
const mockGetReport = healthApi.getReport as ReturnType<typeof vi.fn>;

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(QueryClientProvider, { client: queryClient }, children);
  };
}

describe('useHealthVersion', () => {
  beforeEach(() => { vi.clearAllMocks(); });

  it('returns loading initially', () => {
    mockGetVersion.mockReturnValue(new Promise(() => {}));
    const { result } = renderHook(() => useHealthVersion(), { wrapper: createWrapper() });
    expect(result.current.isLoading).toBe(true);
  });

  it('returns version data on success', async () => {
    const versionInfo = {
      version: '1.2.3',
      informationalVersion: '1.2.3+abc',
      environment: 'Production',
      machineName: 'host-1',
      osDescription: 'Linux',
      frameworkDescription: '.NET 10',
      startedAt: new Date().toISOString(),
    };
    mockGetVersion.mockResolvedValue(versionInfo);
    const { result } = renderHook(() => useHealthVersion(), { wrapper: createWrapper() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(versionInfo);
  });

  it('returns error state when fetch fails', async () => {
    mockGetVersion.mockRejectedValue(new Error('network error'));
    const { result } = renderHook(() => useHealthVersion(), { wrapper: createWrapper() });
    await waitFor(() => expect(result.current.isError).toBe(true), { timeout: 5000 });
    expect(result.current.error).toBeDefined();
  });
});

describe('useHealthStatus', () => {
  beforeEach(() => { vi.clearAllMocks(); });

  it('returns loading initially', () => {
    mockGetStatus.mockReturnValue(new Promise(() => {}));
    const { result } = renderHook(() => useHealthStatus(), { wrapper: createWrapper() });
    expect(result.current.isLoading).toBe(true);
  });

  it('returns status data on success', async () => {
    const statusInfo = {
      isHealthy: true,
      uptime: '1d 2h 3m',
      memoryUsageMb: 128,
      threadCount: 30,
      gcTotalMemoryMb: 64,
      gen0Collections: 100,
      gen1Collections: 10,
      gen2Collections: 1,
      timestamp: new Date().toISOString(),
    };
    mockGetStatus.mockResolvedValue(statusInfo);
    const { result } = renderHook(() => useHealthStatus(), { wrapper: createWrapper() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.isHealthy).toBe(true);
  });

  it('returns error state when fetch fails', async () => {
    mockGetStatus.mockRejectedValue(new Error('service unavailable'));
    const { result } = renderHook(() => useHealthStatus(), { wrapper: createWrapper() });
    await waitFor(() => expect(result.current.isError).toBe(true), { timeout: 5000 });
  });
});

describe('useHealthReport', () => {
  beforeEach(() => { vi.clearAllMocks(); });

  it('returns loading initially', () => {
    mockGetReport.mockReturnValue(new Promise(() => {}));
    const { result } = renderHook(() => useHealthReport(), { wrapper: createWrapper() });
    expect(result.current.isLoading).toBe(true);
  });

  it('returns report data on success', async () => {
    const report = {
      status: 'Degraded',
      totalDuration: 42.5,
      entries: {
        self: { status: 'Healthy', description: 'API is running', duration: 0.1 },
        servicebus: {
          status: 'Degraded',
          description: '1 of 2 Azure Service Bus namespaces are unhealthy.',
          duration: 40.2,
        },
      },
    };
    mockGetReport.mockResolvedValue(report);
    const { result } = renderHook(() => useHealthReport(), { wrapper: createWrapper() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.status).toBe('Degraded');
    expect(result.current.data?.entries.servicebus.status).toBe('Degraded');
  });

  it('returns error state when fetch fails', async () => {
    mockGetReport.mockRejectedValue(new Error('network error'));
    const { result } = renderHook(() => useHealthReport(), { wrapper: createWrapper() });
    await waitFor(() => expect(result.current.isError).toBe(true), { timeout: 5000 });
  });
});
