/**
 * Regression suite: Demo Mode must never issue a real backend network request.
 *
 * Every hook below reaches a real API module under normal operation. Each API module is
 * mocked here so a call would be trivially observable — the assertions verify the mock was
 * never invoked while `DemoModeProvider` is active, covering both auto-firing queries and
 * user-triggered mutations (which must reject locally instead of touching the network).
 */
import { vi, describe, it, expect, beforeEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';

vi.mock('../../lib/api/rules', () => ({
  rulesApi: {
    getAll: vi.fn(),
    getById: vi.fn(),
    getTemplates: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    delete: vi.fn(),
    toggle: vi.fn(),
    test: vi.fn(),
    replayAll: vi.fn(),
    generateRules: vi.fn(),
  },
}));

vi.mock('../../lib/api/scheduled', () => ({
  scheduledApi: { listScheduled: vi.fn(), cancelScheduled: vi.fn() },
}));

vi.mock('../../lib/api/dlqHistory', () => ({
  dlqHistoryApi: {
    getHistory: vi.fn(),
    getById: vi.fn(),
    getTimeline: vi.fn(),
    getSummary: vi.fn(),
    updateNotes: vi.fn(),
    updateStatus: vi.fn(),
  },
}));

vi.mock('../../lib/api/dlqSignatures', () => ({
  dlqSignaturesApi: { getSignatures: vi.fn() },
}));

vi.mock('../../lib/api/fleet', () => ({
  fleetApi: { getOverview: vi.fn() },
}));

vi.mock('../../lib/api/health', () => ({
  healthApi: { getVersion: vi.fn(), getStatus: vi.fn(), getReport: vi.fn() },
}));

vi.mock('../../lib/api/insights', () => ({
  insightsApi: {
    list: vi.fn(),
    get: vi.fn(),
    getSummary: vi.fn(),
    dismiss: vi.fn(),
    resolve: vi.fn(),
    isAvailable: vi.fn(),
  },
}));

vi.mock('../../lib/api/audit', () => ({
  auditApi: { getLogs: vi.fn(), getSummary: vi.fn() },
}));

vi.mock('../../lib/api/bulkOperations', () => ({
  bulkOperationsApi: { preview: vi.fn(), create: vi.fn(), get: vi.fn(), list: vi.fn(), cancel: vi.fn() },
  isTerminalBulkOperationStatus: (status: string) =>
    ['Completed', 'CompletedWithErrors', 'Cancelled', 'Failed'].includes(status),
}));

vi.mock('../../lib/api/crossCloudTrace', () => ({
  crossCloudTraceApi: { trace: vi.fn() },
}));

vi.mock('../../lib/api/namespaces', () => ({
  namespacesApi: { list: vi.fn(), get: vi.fn(), create: vi.fn(), delete: vi.fn(), testConnection: vi.fn() },
}));

vi.mock('../../lib/api/messages', () => ({
  messagesApi: { list: vi.fn(), get: vi.fn(), send: vi.fn(), replay: vi.fn(), purge: vi.fn() },
}));

vi.mock('../../lib/api/liveTail', () => ({
  connectLiveTail: vi.fn(() => () => {}),
}));

vi.mock('../../lib/api/client', () => ({
  apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}));

vi.mock('react-hot-toast', () => ({
  default: { success: vi.fn(), error: vi.fn() },
}));

import { rulesApi } from '../../lib/api/rules';
import { scheduledApi } from '../../lib/api/scheduled';
import { dlqHistoryApi } from '../../lib/api/dlqHistory';
import { dlqSignaturesApi } from '../../lib/api/dlqSignatures';
import { fleetApi } from '../../lib/api/fleet';
import { healthApi } from '../../lib/api/health';
import { insightsApi } from '../../lib/api/insights';
import { auditApi } from '../../lib/api/audit';
import { bulkOperationsApi } from '../../lib/api/bulkOperations';
import { crossCloudTraceApi } from '../../lib/api/crossCloudTrace';
import { namespacesApi } from '../../lib/api/namespaces';
import { messagesApi } from '../../lib/api/messages';
import { connectLiveTail } from '../../lib/api/liveTail';
import { apiClient } from '../../lib/api/client';

import { useRules, useCreateRule, useDeleteRule, useReplayAll, useGenerateRules } from '../../hooks/useRules';
import { useScheduledMessages, useCancelScheduledMessage } from '../../hooks/useScheduledMessages';
import { useDlqHistory, useUpdateDlqStatus } from '../../hooks/useDlqHistory';
import { useDlqSignatures } from '../../hooks/useDlqSignatures';
import { useFleetOverview } from '../../hooks/useFleet';
import { useHealthStatus, useHealthReport } from '../../hooks/useHealth';
import { useInsights, useDismissInsight } from '../../hooks/useInsights';
import { useAuditLogs } from '../../hooks/useAudit';
import { useBulkOperationJobs, useCreateBulkOperation } from '../../hooks/useBulkOperations';
import { useCrossCloudTrace } from '../../hooks/useCrossCloudTrace';
import { useCreateNamespace, useDeleteNamespace } from '../../hooks/useNamespaces';
import { useSendMessage, useReplayMessage, usePurgeMessage } from '../../hooks/useMessages';
import { useLiveTail } from '../../hooks/useLiveTail';
import { useNamespaceStats, useAllNamespacesQueues } from '../../hooks/useQueues';
import { DemoModeProvider } from '../../lib/demo/DemoContext';

function createDemoWrapper(cloudProvider: 'azure' | 'aws' | 'gcp' = 'azure') {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(
      QueryClientProvider,
      { client: queryClient },
      React.createElement(DemoModeProvider, { cloudProvider, children }),
    );
  };
}

beforeEach(() => vi.clearAllMocks());

describe('Demo Mode network isolation — query hooks never call the real API', () => {
  it('useRules does not call rulesApi.getAll', async () => {
    renderHook(() => useRules(), { wrapper: createDemoWrapper() });
    await waitFor(() => expect(rulesApi.getAll).not.toHaveBeenCalled());
  });

  it('useScheduledMessages does not call scheduledApi.listScheduled', async () => {
    renderHook(() => useScheduledMessages('demo-azure-contoso-prod', 'orders-queue'), {
      wrapper: createDemoWrapper(),
    });
    await waitFor(() => expect(scheduledApi.listScheduled).not.toHaveBeenCalled());
  });

  it('useDlqHistory does not call dlqHistoryApi.getHistory', async () => {
    renderHook(() => useDlqHistory({}), { wrapper: createDemoWrapper() });
    await waitFor(() => expect(dlqHistoryApi.getHistory).not.toHaveBeenCalled());
  });

  it('useDlqSignatures does not call dlqSignaturesApi.getSignatures', async () => {
    renderHook(() => useDlqSignatures('demo-azure-contoso-prod'), { wrapper: createDemoWrapper() });
    await waitFor(() => expect(dlqSignaturesApi.getSignatures).not.toHaveBeenCalled());
  });

  it('useFleetOverview does not call fleetApi.getOverview', async () => {
    renderHook(() => useFleetOverview(), { wrapper: createDemoWrapper() });
    await waitFor(() => expect(fleetApi.getOverview).not.toHaveBeenCalled());
  });

  it('useHealthStatus/useHealthReport do not call healthApi', async () => {
    renderHook(() => useHealthStatus(), { wrapper: createDemoWrapper() });
    renderHook(() => useHealthReport(), { wrapper: createDemoWrapper() });
    await waitFor(() => {
      expect(healthApi.getStatus).not.toHaveBeenCalled();
      expect(healthApi.getReport).not.toHaveBeenCalled();
    });
  });

  it('useInsights does not call insightsApi.list', async () => {
    renderHook(() => useInsights({ namespaceId: 'demo-azure-contoso-prod' }), {
      wrapper: createDemoWrapper(),
    });
    await waitFor(() => expect(insightsApi.list).not.toHaveBeenCalled());
  });

  it('useAuditLogs does not call auditApi.getLogs', async () => {
    renderHook(() => useAuditLogs({}), { wrapper: createDemoWrapper() });
    await waitFor(() => expect(auditApi.getLogs).not.toHaveBeenCalled());
  });

  it('useBulkOperationJobs does not call bulkOperationsApi.list', async () => {
    renderHook(() => useBulkOperationJobs(), { wrapper: createDemoWrapper() });
    await waitFor(() => expect(bulkOperationsApi.list).not.toHaveBeenCalled());
  });

  it('useNamespaceStats returns mock stats without calling apiClient.get', async () => {
    const { result } = renderHook(() => useNamespaceStats(['demo-azure-contoso-prod']), {
      wrapper: createDemoWrapper(),
    });
    await waitFor(() => expect(result.current[0]?.data).toBeDefined());
    expect(apiClient.get).not.toHaveBeenCalled();
  });

  it('useAllNamespacesQueues returns mock queues without calling apiClient.get', async () => {
    const { result } = renderHook(() => useAllNamespacesQueues(['demo-azure-contoso-prod']), {
      wrapper: createDemoWrapper(),
    });
    await waitFor(() => expect(result.current[0]?.queues).toBeDefined());
    expect(apiClient.get).not.toHaveBeenCalled();
  });
});

describe('Demo Mode network isolation — mutations reject locally instead of calling the real API', () => {
  it('useCreateRule rejects without calling rulesApi.create', async () => {
    const { result } = renderHook(() => useCreateRule(), { wrapper: createDemoWrapper() });
    await act(async () => {
      result.current.mutate({ name: 'x' } as any);
    });
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(rulesApi.create).not.toHaveBeenCalled();
  });

  it('useDeleteRule rejects without calling rulesApi.delete', async () => {
    const { result } = renderHook(() => useDeleteRule(), { wrapper: createDemoWrapper() });
    await act(async () => {
      result.current.mutate(1);
    });
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(rulesApi.delete).not.toHaveBeenCalled();
  });

  it('useReplayAll rejects without calling rulesApi.replayAll', async () => {
    const { result } = renderHook(() => useReplayAll(), { wrapper: createDemoWrapper() });
    await act(async () => {
      result.current.mutate(1);
    });
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(rulesApi.replayAll).not.toHaveBeenCalled();
  });

  it('useGenerateRules rejects without calling rulesApi.generateRules', async () => {
    const { result } = renderHook(() => useGenerateRules(), { wrapper: createDemoWrapper() });
    await act(async () => {
      result.current.mutate(undefined);
    });
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(rulesApi.generateRules).not.toHaveBeenCalled();
  });

  it('useCancelScheduledMessage rejects without calling scheduledApi.cancelScheduled', async () => {
    const { result } = renderHook(() => useCancelScheduledMessage(), { wrapper: createDemoWrapper() });
    await act(async () => {
      result.current.mutate({ namespaceId: 'ns', queueName: 'q', sequenceNumber: 1 });
    });
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(scheduledApi.cancelScheduled).not.toHaveBeenCalled();
  });

  it('useUpdateDlqStatus rejects without calling dlqHistoryApi.updateStatus', async () => {
    const { result } = renderHook(() => useUpdateDlqStatus(), { wrapper: createDemoWrapper() });
    await act(async () => {
      result.current.mutate({ id: 1, status: 'Resolved' });
    });
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(dlqHistoryApi.updateStatus).not.toHaveBeenCalled();
  });

  it('useDismissInsight rejects without calling insightsApi.dismiss', async () => {
    const { result } = renderHook(() => useDismissInsight(), { wrapper: createDemoWrapper() });
    await act(async () => {
      result.current.mutate({ namespaceId: 'ns', insightId: '1' });
    });
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(insightsApi.dismiss).not.toHaveBeenCalled();
  });

  it('useCreateBulkOperation rejects without calling bulkOperationsApi.create', async () => {
    const { result } = renderHook(() => useCreateBulkOperation(), { wrapper: createDemoWrapper() });
    await act(async () => {
      result.current.mutate({ operationType: 'Replay', filter: {} as any });
    });
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(bulkOperationsApi.create).not.toHaveBeenCalled();
  });

  it('useCrossCloudTrace rejects without calling crossCloudTraceApi.trace', async () => {
    const { result } = renderHook(() => useCrossCloudTrace(), { wrapper: createDemoWrapper() });
    await act(async () => {
      result.current.mutate('trace-id');
    });
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(crossCloudTraceApi.trace).not.toHaveBeenCalled();
  });

  it('useCreateNamespace / useDeleteNamespace reject without calling namespacesApi', async () => {
    const { result: createResult } = renderHook(() => useCreateNamespace(), { wrapper: createDemoWrapper() });
    await act(async () => {
      createResult.current.mutate({} as any);
    });
    await waitFor(() => expect(createResult.current.isError).toBe(true));

    const { result: deleteResult } = renderHook(() => useDeleteNamespace(), { wrapper: createDemoWrapper() });
    await act(async () => {
      deleteResult.current.mutate('ns-id');
    });
    await waitFor(() => expect(deleteResult.current.isError).toBe(true));

    expect(namespacesApi.create).not.toHaveBeenCalled();
    expect(namespacesApi.delete).not.toHaveBeenCalled();
  });

  it('useSendMessage / useReplayMessage / usePurgeMessage reject without calling messagesApi', async () => {
    const { result: sendResult } = renderHook(() => useSendMessage(), { wrapper: createDemoWrapper() });
    await act(async () => {
      sendResult.current.mutate({ namespaceId: 'ns', queueOrTopicName: 'q', message: { body: 'x' } });
    });
    await waitFor(() => expect(sendResult.current.isError).toBe(true));

    const { result: replayResult } = renderHook(() => useReplayMessage(), { wrapper: createDemoWrapper() });
    await act(async () => {
      replayResult.current.mutate({ namespaceId: 'ns', sequenceNumber: 1, entityName: 'q' });
    });
    await waitFor(() => expect(replayResult.current.isError).toBe(true));

    const { result: purgeResult } = renderHook(() => usePurgeMessage(), { wrapper: createDemoWrapper() });
    await act(async () => {
      purgeResult.current.mutate({ namespaceId: 'ns', sequenceNumber: 1, entityName: 'q' });
    });
    await waitFor(() => expect(purgeResult.current.isError).toBe(true));

    expect(messagesApi.send).not.toHaveBeenCalled();
    expect(messagesApi.replay).not.toHaveBeenCalled();
    expect(messagesApi.purge).not.toHaveBeenCalled();
  });
});

describe('Demo Mode network isolation — Live Tail never opens a real SSE connection', () => {
  it('start() marks the stream unsupported instead of calling connectLiveTail', async () => {
    const { result } = renderHook(
      () => useLiveTail({ namespaceId: 'demo-azure-contoso-prod', entityName: 'orders-queue' }),
      { wrapper: createDemoWrapper() },
    );

    act(() => {
      result.current.start();
    });

    await waitFor(() => expect(result.current.status).toBe('unsupported'));
    expect(connectLiveTail).not.toHaveBeenCalled();
  });
});
