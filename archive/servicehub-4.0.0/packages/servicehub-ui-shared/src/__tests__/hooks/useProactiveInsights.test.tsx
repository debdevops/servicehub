import { vi, describe, it, expect, beforeEach } from 'vitest';
import { renderHook, act, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';

vi.mock('../../lib/api/proactiveInsights', () => ({
  narrationsApi: { generate: vi.fn() },
  correlationFindingsApi: { detect: vi.fn() },
  backlogForecastsApi: { forecast: vi.fn() },
  driftFindingsApi: { exportContractViolations: vi.fn() },
}));

vi.mock('react-hot-toast', () => ({
  default: { error: vi.fn(), success: vi.fn() },
}));

const mockIsDemoMode = vi.fn(() => false);
vi.mock('../../lib/demo/DemoContext', async () => {
  const actual = await vi.importActual<typeof import('../../lib/demo/DemoContext')>('../../lib/demo/DemoContext');
  return {
    ...actual,
    useDemoContext: () => ({ isDemoMode: mockIsDemoMode() }),
  };
});

import {
  narrationsApi,
  correlationFindingsApi,
  backlogForecastsApi,
  driftFindingsApi,
} from '../../lib/api/proactiveInsights';
import toast from 'react-hot-toast';
import {
  useGenerateNarrations,
  useDetectCorrelationFindings,
  useForecastBacklog,
  useExportContractViolations,
} from '../../hooks/useProactiveInsights';

const mockGenerate = narrationsApi.generate as ReturnType<typeof vi.fn>;
const mockDetect = correlationFindingsApi.detect as ReturnType<typeof vi.fn>;
const mockForecast = backlogForecastsApi.forecast as ReturnType<typeof vi.fn>;
const mockExport = driftFindingsApi.exportContractViolations as ReturnType<typeof vi.fn>;
const mockToastError = toast.error as ReturnType<typeof vi.fn>;

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(QueryClientProvider, { client: queryClient }, children);
  };
}

describe('useGenerateNarrations', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockIsDemoMode.mockReturnValue(false);
  });

  it('calls narrationsApi.generate and returns the response on success', async () => {
    const response = { startTime: '', endTime: '', narrations: [{ id: 'n1' }], generatedAt: '' };
    mockGenerate.mockResolvedValueOnce(response);
    const { result } = renderHook(() => useGenerateNarrations(), { wrapper: createWrapper() });

    await act(async () => { result.current.mutate(); });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockGenerate).toHaveBeenCalledWith({});
    expect(result.current.data).toEqual(response);
  });

  it('rejects and toasts without calling the API in demo mode', async () => {
    mockIsDemoMode.mockReturnValue(true);
    const { result } = renderHook(() => useGenerateNarrations(), { wrapper: createWrapper() });

    await act(async () => { result.current.mutate(); });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(mockGenerate).not.toHaveBeenCalled();
    expect(mockToastError).toHaveBeenCalled();
  });

  it('shows the API error detail on failure', async () => {
    mockGenerate.mockRejectedValueOnce({ response: { data: { detail: 'Analysis window invalid' } } });
    const { result } = renderHook(() => useGenerateNarrations(), { wrapper: createWrapper() });

    await act(async () => { result.current.mutate(); });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(mockToastError).toHaveBeenCalledWith('Analysis window invalid', { duration: 6000 });
  });
});

describe('useDetectCorrelationFindings', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockIsDemoMode.mockReturnValue(false);
  });

  it('calls correlationFindingsApi.detect on success', async () => {
    const response = { startTime: '', endTime: '', findings: [], detectedAt: '' };
    mockDetect.mockResolvedValueOnce(response);
    const { result } = renderHook(() => useDetectCorrelationFindings(), { wrapper: createWrapper() });

    await act(async () => { result.current.mutate(); });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockDetect).toHaveBeenCalledWith({});
  });
});

describe('useForecastBacklog', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockIsDemoMode.mockReturnValue(false);
  });

  it('calls backlogForecastsApi.forecast with the given namespaceId', async () => {
    const response = { namespaceId: 'ns1', startTime: '', endTime: '', forecasts: [], detectedAt: '' };
    mockForecast.mockResolvedValueOnce(response);
    const { result } = renderHook(() => useForecastBacklog(), { wrapper: createWrapper() });

    await act(async () => { result.current.mutate({ namespaceId: 'ns1' }); });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockForecast).toHaveBeenCalledWith({ namespaceId: 'ns1' });
  });

  it('rejects in demo mode without calling the API', async () => {
    mockIsDemoMode.mockReturnValue(true);
    const { result } = renderHook(() => useForecastBacklog(), { wrapper: createWrapper() });

    await act(async () => { result.current.mutate({ namespaceId: 'ns1' }); });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(mockForecast).not.toHaveBeenCalled();
  });
});

describe('useExportContractViolations', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockIsDemoMode.mockReturnValue(false);
  });

  it('calls driftFindingsApi.exportContractViolations with the given namespaceId', async () => {
    const response = { namespaceId: 'ns1', namespaceName: 'NS', startTime: '', endTime: '', generatedAt: '', violations: [], markdownReport: '' };
    mockExport.mockResolvedValueOnce(response);
    const { result } = renderHook(() => useExportContractViolations(), { wrapper: createWrapper() });

    await act(async () => { result.current.mutate({ namespaceId: 'ns1' }); });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockExport).toHaveBeenCalledWith({ namespaceId: 'ns1' });
  });
});
