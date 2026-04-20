import { vi, describe, it, expect, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import { useDlqHistory, useDlqMessageDetail, useDlqSummary } from '@/hooks/useDlqHistory';
import { useRules, useRuleTemplates } from '@/hooks/useRules';
import { useMessages } from '@/hooks/useMessages';
import { useInsights } from '@/hooks/useInsights';

// Mock all API modules
vi.mock('@/lib/api/dlqHistory', () => ({
  dlqHistoryApi: {
    getHistory: vi.fn(),
    getById: vi.fn(),
    getTimeline: vi.fn(),
    getSummary: vi.fn(),
    updateNotes: vi.fn(),
    setStatus: vi.fn(),
    batchSetStatus: vi.fn(),
    exportCsv: vi.fn(),
  },
}));

vi.mock('@/lib/api/rules', () => ({
  rulesApi: {
    getAll: vi.fn(),
    getById: vi.fn(),
    getTemplates: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    delete: vi.fn(),
    test: vi.fn(),
  },
}));

vi.mock('@/lib/api/messages', () => ({
  messagesApi: {
    list: vi.fn(),
    get: vi.fn(),
    send: vi.fn(),
    replay: vi.fn(),
  },
}));

vi.mock('@/lib/api/insights', () => ({
  insightsApi: {
    list: vi.fn(),
    get: vi.fn(),
    getSummary: vi.fn(),
  },
}));

vi.mock('react-hot-toast', () => ({
  default: { success: vi.fn(), error: vi.fn() },
}));

import { dlqHistoryApi } from '@/lib/api/dlqHistory';
import { rulesApi } from '@/lib/api/rules';
import { messagesApi } from '@/lib/api/messages';
import { insightsApi } from '@/lib/api/insights';

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(QueryClientProvider, { client: queryClient }, children);
  };
}

// ─── useDlqHistory ─────────────────────────────────────────────────

describe('useDlqHistory', () => {
  beforeEach(() => vi.clearAllMocks());

  it('fetches DLQ history and returns data', async () => {
    const mockItems = [{ id: 1, messageId: 'msg-1', status: 'active' }];
    vi.mocked(dlqHistoryApi.getHistory).mockResolvedValueOnce({
      items: mockItems,
      totalCount: 1,
      page: 1,
      pageSize: 25,
      hasNextPage: false,
      hasPreviousPage: false,
    } as any);

    const { result } = renderHook(
      () => useDlqHistory({ namespaceId: 'ns-1' }),
      { wrapper: createWrapper() }
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.items).toHaveLength(1);
    expect(dlqHistoryApi.getHistory).toHaveBeenCalledWith({ namespaceId: 'ns-1' });
  });

  it('is disabled when enabled=false', async () => {
    const { result } = renderHook(
      () => useDlqHistory({ namespaceId: 'ns-1' }, false),
      { wrapper: createWrapper() }
    );

    expect(result.current.fetchStatus).toBe('idle');
    expect(dlqHistoryApi.getHistory).not.toHaveBeenCalled();
  });

  it('handles API error gracefully', async () => {
    // Use 404 so the hook's custom retry fn short-circuits (no retries)
    vi.mocked(dlqHistoryApi.getHistory).mockRejectedValueOnce({ response: { status: 404 } });

    const { result } = renderHook(
      () => useDlqHistory({ namespaceId: 'ns-1' }),
      { wrapper: createWrapper() }
    );

    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});

// ─── useDlqMessageDetail ───────────────────────────────────────────

describe('useDlqMessageDetail', () => {
  beforeEach(() => vi.clearAllMocks());

  it('fetches message detail when id is provided', async () => {
    const detail = { id: 1, messageId: 'msg-1', replayHistory: [] };
    vi.mocked(dlqHistoryApi.getById).mockResolvedValueOnce(detail as any);

    const { result } = renderHook(
      () => useDlqMessageDetail(1),
      { wrapper: createWrapper() }
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(detail);
  });

  it('is disabled when id is null', () => {
    const { result } = renderHook(
      () => useDlqMessageDetail(null),
      { wrapper: createWrapper() }
    );

    expect(result.current.fetchStatus).toBe('idle');
    expect(dlqHistoryApi.getById).not.toHaveBeenCalled();
  });
});

// ─── useDlqSummary ─────────────────────────────────────────────────

describe('useDlqSummary', () => {
  beforeEach(() => vi.clearAllMocks());

  it('fetches summary when namespaceId is provided', async () => {
    const summary = { totalMessages: 10, activeMessages: 5, replayedMessages: 5 };
    vi.mocked(dlqHistoryApi.getSummary).mockResolvedValueOnce(summary as any);

    const { result } = renderHook(
      () => useDlqSummary('ns-1'),
      { wrapper: createWrapper() }
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(summary);
  });

  it('is disabled when namespaceId is undefined', () => {
    const { result } = renderHook(
      () => useDlqSummary(undefined),
      { wrapper: createWrapper() }
    );

    expect(result.current.fetchStatus).toBe('idle');
  });
});

// ─── useRules ──────────────────────────────────────────────────────

describe('useRules', () => {
  beforeEach(() => vi.clearAllMocks());

  it('fetches all rules', async () => {
    const rules = [{ id: 1, name: 'Test Rule', isEnabled: true }];
    vi.mocked(rulesApi.getAll).mockResolvedValueOnce(rules as any);

    const { result } = renderHook(
      () => useRules(),
      { wrapper: createWrapper() }
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(rules);
  });

  it('passes enabledOnly filter to API', async () => {
    vi.mocked(rulesApi.getAll).mockResolvedValueOnce([] as any);

    renderHook(() => useRules(true), { wrapper: createWrapper() });

    await waitFor(() => expect(rulesApi.getAll).toHaveBeenCalledWith(true));
  });
});

// ─── useRuleTemplates ──────────────────────────────────────────────

describe('useRuleTemplates', () => {
  beforeEach(() => vi.clearAllMocks());

  it('fetches rule templates', async () => {
    const templates = [{ id: 'tmpl-1', name: 'Template 1' }];
    vi.mocked(rulesApi.getTemplates).mockResolvedValueOnce(templates as any);

    const { result } = renderHook(
      () => useRuleTemplates(),
      { wrapper: createWrapper() }
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(templates);
  });
});

// ─── useMessages ───────────────────────────────────────────────────

describe('useMessages', () => {
  beforeEach(() => vi.clearAllMocks());

  it('fetches messages for a queue', async () => {
    const messages = { items: [{ id: 1, body: 'hello' }], totalCount: 1, hasMore: false };
    vi.mocked(messagesApi.list).mockResolvedValueOnce(messages as any);

    const { result } = renderHook(
      () => useMessages({ namespaceId: 'ns-1', queueOrTopicName: 'my-queue', entityType: 'queue' }),
      { wrapper: createWrapper() }
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(messages);
  });

  it('is disabled when namespaceId is empty', () => {
    const { result } = renderHook(
      () => useMessages({ namespaceId: '', queueOrTopicName: 'my-queue', entityType: 'queue' }),
      { wrapper: createWrapper() }
    );

    expect(result.current.fetchStatus).toBe('idle');
    expect(messagesApi.list).not.toHaveBeenCalled();
  });

  it('is disabled when queueOrTopicName is empty', () => {
    const { result } = renderHook(
      () => useMessages({ namespaceId: 'ns-1', queueOrTopicName: '', entityType: 'queue' }),
      { wrapper: createWrapper() }
    );

    expect(result.current.fetchStatus).toBe('idle');
    expect(messagesApi.list).not.toHaveBeenCalled();
  });

  it('returns empty result on 404 instead of error', async () => {
    const error404 = { response: { status: 404 } };
    vi.mocked(messagesApi.list).mockRejectedValueOnce(error404);

    const { result } = renderHook(
      () => useMessages({ namespaceId: 'ns-1', queueOrTopicName: 'my-queue', entityType: 'queue' }),
      { wrapper: createWrapper() }
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual({ items: [], totalCount: 0, hasMore: false });
  });
});

// ─── useInsights ───────────────────────────────────────────────────

describe('useInsights', () => {
  beforeEach(() => vi.clearAllMocks());

  it('fetches insights when namespaceId is provided', async () => {
    const insights = [{ id: 'i-1', type: 'pattern' }];
    vi.mocked(insightsApi.list).mockResolvedValueOnce(insights as any);

    const { result } = renderHook(
      () => useInsights({ namespaceId: 'ns-1' }),
      { wrapper: createWrapper() }
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(insights);
  });

  it('is disabled when namespaceId is empty', () => {
    const { result } = renderHook(
      () => useInsights({ namespaceId: '' }),
      { wrapper: createWrapper() }
    );

    expect(result.current.fetchStatus).toBe('idle');
    expect(insightsApi.list).not.toHaveBeenCalled();
  });
});
