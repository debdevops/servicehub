import { vi, describe, it, expect, beforeEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';

vi.mock('@/lib/api/insights', () => ({
  insightsApi: {
    list: vi.fn(),
    get: vi.fn(),
    getSummary: vi.fn(),
    dismiss: vi.fn(),
    resolve: vi.fn(),
    isAvailable: vi.fn(),
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
    toggle: vi.fn(),
    test: vi.fn(),
    replayAll: vi.fn(),
  },
}));

vi.mock('@/lib/api/namespaces', () => ({
  namespacesApi: {
    list: vi.fn(),
    get: vi.fn(),
    create: vi.fn(),
    delete: vi.fn(),
    testConnection: vi.fn(),
  },
}));

vi.mock('@/lib/api/messages', () => ({
  messagesApi: {
    list: vi.fn(),
    get: vi.fn(),
    send: vi.fn(),
    replay: vi.fn(),
    purge: vi.fn(),
    getQueueTabCounts: vi.fn(),
  },
}));

vi.mock('@/lib/ai/analyzer', () => ({
  analyzeMessages: vi.fn(),
  isAIAvailable: vi.fn(() => false),
}));

vi.mock('react-hot-toast', () => ({
  default: { success: vi.fn(), error: vi.fn() },
}));

import { insightsApi } from '@/lib/api/insights';
import { rulesApi } from '@/lib/api/rules';
import { namespacesApi } from '@/lib/api/namespaces';
import toast from 'react-hot-toast';

import {
  useInsights,
  useInsight,
  useInsightsSummary,
  useClientSideInsights,
  useDismissInsight,
  useResolveInsight,
  useAIAvailability,
} from '@/hooks/useInsights';

import {
  useRules,
  useRule,
  useRuleTemplates,
  useCreateRule,
  useUpdateRule,
  useDeleteRule,
  useToggleRule,
  useTestRule,
  useReplayAll,
} from '@/hooks/useRules';

import {
  useNamespaces,
  useNamespace,
  useCreateNamespace,
  useDeleteNamespace,
  useTestConnection,
} from '@/hooks/useNamespaces';

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(QueryClientProvider, { client: queryClient }, children);
  };
}

// ─── useInsights ──────────────────────────────────────────────────────────────

describe('useInsights', () => {
  beforeEach(() => vi.clearAllMocks());

  it('fetches insights when namespaceId provided', async () => {
    const mockInsights = [{ id: 'ins-1', title: 'Issue A' }];
    vi.mocked(insightsApi.list).mockResolvedValueOnce(mockInsights as any);

    const { result } = renderHook(
      () => useInsights({ namespaceId: 'ns-1' }),
      { wrapper: createWrapper() }
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(mockInsights);
  });

  it('is disabled when namespaceId is empty', () => {
    const { result } = renderHook(
      () => useInsights({ namespaceId: '' }),
      { wrapper: createWrapper() }
    );

    expect(result.current.fetchStatus).toBe('idle');
    expect(insightsApi.list).not.toHaveBeenCalled();
  });

  it('handles API error', async () => {
    vi.mocked(insightsApi.list).mockRejectedValueOnce(new Error('Network error'));

    const { result } = renderHook(
      () => useInsights({ namespaceId: 'ns-1' }),
      { wrapper: createWrapper() }
    );

    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});

// ─── useInsight (single) ─────────────────────────────────────────────────────

describe('useInsight', () => {
  beforeEach(() => vi.clearAllMocks());

  it('fetches single insight when both IDs provided', async () => {
    const mockInsight = { id: 'ins-abc', title: 'Pattern X' };
    vi.mocked(insightsApi.get).mockResolvedValueOnce(mockInsight as any);

    const { result } = renderHook(
      () => useInsight('ns-1', 'ins-abc'),
      { wrapper: createWrapper() }
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(mockInsight);
  });

  it('is disabled when namespaceId is empty', () => {
    const { result } = renderHook(
      () => useInsight('', 'ins-abc'),
      { wrapper: createWrapper() }
    );

    expect(result.current.fetchStatus).toBe('idle');
  });

  it('is disabled when insightId is empty', () => {
    const { result } = renderHook(
      () => useInsight('ns-1', ''),
      { wrapper: createWrapper() }
    );

    expect(result.current.fetchStatus).toBe('idle');
  });
});

// ─── useInsightsSummary ──────────────────────────────────────────────────────

describe('useInsightsSummary', () => {
  beforeEach(() => vi.clearAllMocks());

  it('fetches summary when both params provided', async () => {
    const mockSummary = { total: 5, critical: 1 };
    vi.mocked(insightsApi.getSummary).mockResolvedValueOnce(mockSummary as any);

    const { result } = renderHook(
      () => useInsightsSummary('ns-1', 'orders-queue'),
      { wrapper: createWrapper() }
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(mockSummary);
  });

  it('is disabled when namespaceId is empty', () => {
    const { result } = renderHook(
      () => useInsightsSummary('', 'orders-queue'),
      { wrapper: createWrapper() }
    );

    expect(result.current.fetchStatus).toBe('idle');
  });

  it('is disabled when entityName is empty', () => {
    const { result } = renderHook(
      () => useInsightsSummary('ns-1', ''),
      { wrapper: createWrapper() }
    );

    expect(result.current.fetchStatus).toBe('idle');
  });
});

// ─── useClientSideInsights ──────────────────────────────────────────────────

describe('useClientSideInsights', () => {
  beforeEach(() => vi.clearAllMocks());

  it('is disabled when isAIAvailable returns false', () => {
    const context = { namespaceId: 'ns-1', entityName: 'q', entityType: 'queue' as const };
    const messages = [{ id: 'msg-1', body: 'test' }] as any;

    const { result } = renderHook(
      () => useClientSideInsights(messages, context, true),
      { wrapper: createWrapper() }
    );

    expect(result.current.fetchStatus).toBe('idle');
  });

  it('is disabled when messages is undefined', () => {
    const context = { namespaceId: 'ns-1', entityName: 'q', entityType: 'queue' as const };

    const { result } = renderHook(
      () => useClientSideInsights(undefined, context, true),
      { wrapper: createWrapper() }
    );

    expect(result.current.fetchStatus).toBe('idle');
  });

  it('is disabled when enabled=false', () => {
    const context = { namespaceId: 'ns-1', entityName: 'q', entityType: 'queue' as const };
    const messages = [{ id: 'msg-1' }] as any;

    const { result } = renderHook(
      () => useClientSideInsights(messages, context, false),
      { wrapper: createWrapper() }
    );

    expect(result.current.fetchStatus).toBe('idle');
  });
});

// ─── useDismissInsight ───────────────────────────────────────────────────────

describe('useDismissInsight', () => {
  beforeEach(() => vi.clearAllMocks());

  it('calls dismiss API and shows success toast', async () => {
    vi.mocked(insightsApi.dismiss).mockResolvedValueOnce(undefined as any);

    const { result } = renderHook(() => useDismissInsight(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({ namespaceId: 'ns-1', insightId: 'ins-1' });
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(toast.success).toHaveBeenCalledWith('Insight dismissed');
  });

  it('shows error toast on failure', async () => {
    vi.mocked(insightsApi.dismiss).mockRejectedValueOnce(new Error('fail'));

    const { result } = renderHook(() => useDismissInsight(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({ namespaceId: 'ns-1', insightId: 'ins-1' });
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(toast.error).toHaveBeenCalledWith(
      expect.stringContaining('Failed to dismiss insight'),
      expect.any(Object)
    );
  });
});

// ─── useResolveInsight ───────────────────────────────────────────────────────

describe('useResolveInsight', () => {
  beforeEach(() => vi.clearAllMocks());

  it('calls resolve API and shows success toast', async () => {
    vi.mocked(insightsApi.resolve).mockResolvedValueOnce(undefined as any);

    const { result } = renderHook(() => useResolveInsight(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({ namespaceId: 'ns-1', insightId: 'ins-1' });
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(toast.success).toHaveBeenCalledWith('Insight marked as resolved');
  });

  it('shows error toast on failure', async () => {
    vi.mocked(insightsApi.resolve).mockRejectedValueOnce(new Error('fail'));

    const { result } = renderHook(() => useResolveInsight(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({ namespaceId: 'ns-1', insightId: 'ins-1' });
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(toast.error).toHaveBeenCalledWith(
      expect.stringContaining('Failed to resolve insight'),
      expect.any(Object)
    );
  });
});

// ─── useAIAvailability ───────────────────────────────────────────────────────

describe('useAIAvailability', () => {
  beforeEach(() => vi.clearAllMocks());

  it('fetches AI availability', async () => {
    vi.mocked(insightsApi.isAvailable).mockResolvedValueOnce(true as any);

    const { result } = renderHook(() => useAIAvailability(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toBe(true);
  });
});

// ─── useRules ────────────────────────────────────────────────────────────────

describe('useRules', () => {
  beforeEach(() => vi.clearAllMocks());

  it('fetches all rules', async () => {
    const mockRules = [{ id: 1, name: 'Rule A', enabled: true }];
    vi.mocked(rulesApi.getAll).mockResolvedValueOnce(mockRules as any);

    const { result } = renderHook(
      () => useRules(),
      { wrapper: createWrapper() }
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(mockRules);
  });

  it('fetches enabled-only rules when flag set', async () => {
    vi.mocked(rulesApi.getAll).mockResolvedValueOnce([] as any);

    const { result } = renderHook(
      () => useRules(true),
      { wrapper: createWrapper() }
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(rulesApi.getAll).toHaveBeenCalledWith(true);
  });
});

// ─── useRule (single) ────────────────────────────────────────────────────────

describe('useRule', () => {
  beforeEach(() => vi.clearAllMocks());

  it('fetches rule by ID', async () => {
    const mockRule = { id: 5, name: 'My Rule' };
    vi.mocked(rulesApi.getById).mockResolvedValueOnce(mockRule as any);

    const { result } = renderHook(
      () => useRule(5),
      { wrapper: createWrapper() }
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(mockRule);
  });

  it('is disabled when id is null', () => {
    const { result } = renderHook(
      () => useRule(null),
      { wrapper: createWrapper() }
    );

    expect(result.current.fetchStatus).toBe('idle');
    expect(rulesApi.getById).not.toHaveBeenCalled();
  });
});

// ─── useRuleTemplates ────────────────────────────────────────────────────────

describe('useRuleTemplates', () => {
  beforeEach(() => vi.clearAllMocks());

  it('fetches rule templates', async () => {
    const mockTemplates = [{ id: 't1', name: 'Template 1' }];
    vi.mocked(rulesApi.getTemplates).mockResolvedValueOnce(mockTemplates as any);

    const { result } = renderHook(
      () => useRuleTemplates(),
      { wrapper: createWrapper() }
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(mockTemplates);
  });
});

// ─── useCreateRule ───────────────────────────────────────────────────────────

describe('useCreateRule', () => {
  beforeEach(() => vi.clearAllMocks());

  it('creates a rule and shows success toast', async () => {
    const newRule = { id: 10, name: 'New Rule', enabled: true };
    vi.mocked(rulesApi.create).mockResolvedValueOnce(newRule as any);

    const { result } = renderHook(() => useCreateRule(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({ name: 'New Rule' } as any);
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(toast.success).toHaveBeenCalledWith(`Rule "New Rule" created`);
  });

  it('shows error toast on failure', async () => {
    vi.mocked(rulesApi.create).mockRejectedValueOnce(new Error('fail'));

    const { result } = renderHook(() => useCreateRule(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({ name: 'New Rule' } as any);
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(toast.error).toHaveBeenCalledWith('Failed to create rule');
  });
});

// ─── useUpdateRule ───────────────────────────────────────────────────────────

describe('useUpdateRule', () => {
  beforeEach(() => vi.clearAllMocks());

  it('updates rule and shows success toast', async () => {
    const updatedRule = { id: 5, name: 'Updated Rule', enabled: true };
    vi.mocked(rulesApi.update).mockResolvedValueOnce(updatedRule as any);

    const { result } = renderHook(() => useUpdateRule(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({ id: 5, request: { name: 'Updated Rule' } as any });
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(toast.success).toHaveBeenCalledWith(`Rule "Updated Rule" updated`);
  });

  it('shows error toast on failure', async () => {
    vi.mocked(rulesApi.update).mockRejectedValueOnce(new Error('fail'));

    const { result } = renderHook(() => useUpdateRule(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({ id: 5, request: { name: 'Rule' } as any });
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(toast.error).toHaveBeenCalledWith('Failed to update rule');
  });
});

// ─── useDeleteRule ───────────────────────────────────────────────────────────

describe('useDeleteRule', () => {
  beforeEach(() => vi.clearAllMocks());

  it('deletes rule and shows success toast', async () => {
    vi.mocked(rulesApi.delete).mockResolvedValueOnce(undefined as any);

    const { result } = renderHook(() => useDeleteRule(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate(5);
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(toast.success).toHaveBeenCalledWith('Rule deleted');
  });

  it('shows error toast on failure', async () => {
    vi.mocked(rulesApi.delete).mockRejectedValueOnce(new Error('fail'));

    const { result } = renderHook(() => useDeleteRule(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate(5);
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(toast.error).toHaveBeenCalledWith('Failed to delete rule');
  });
});

// ─── useToggleRule ───────────────────────────────────────────────────────────

describe('useToggleRule', () => {
  beforeEach(() => vi.clearAllMocks());

  it('toggles rule and shows enabled toast', async () => {
    const updatedRule = { id: 3, name: 'My Rule', enabled: true };
    vi.mocked(rulesApi.toggle).mockResolvedValueOnce(updatedRule as any);

    const { result } = renderHook(() => useToggleRule(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate(3);
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(toast.success).toHaveBeenCalledWith(`Rule "My Rule" enabled`);
  });

  it('shows disabled toast when toggled off', async () => {
    const updatedRule = { id: 3, name: 'My Rule', enabled: false };
    vi.mocked(rulesApi.toggle).mockResolvedValueOnce(updatedRule as any);

    const { result } = renderHook(() => useToggleRule(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate(3);
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(toast.success).toHaveBeenCalledWith(`Rule "My Rule" disabled`);
  });

  it('shows error toast on failure', async () => {
    vi.mocked(rulesApi.toggle).mockRejectedValueOnce(new Error('fail'));

    const { result } = renderHook(() => useToggleRule(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate(3);
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(toast.error).toHaveBeenCalledWith('Failed to toggle rule');
  });
});

// ─── useTestRule ─────────────────────────────────────────────────────────────

describe('useTestRule', () => {
  beforeEach(() => vi.clearAllMocks());

  it('tests rule and returns results', async () => {
    const testResult = { matched: 5, messages: [] };
    vi.mocked(rulesApi.test).mockResolvedValueOnce(testResult as any);

    const { result } = renderHook(() => useTestRule(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({ ruleId: 1 } as any);
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(testResult);
  });

  it('shows error toast on failure', async () => {
    vi.mocked(rulesApi.test).mockRejectedValueOnce(new Error('fail'));

    const { result } = renderHook(() => useTestRule(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({ ruleId: 1 } as any);
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(toast.error).toHaveBeenCalledWith('Failed to test rule');
  });
});

// ─── useReplayAll ────────────────────────────────────────────────────────────

describe('useReplayAll', () => {
  beforeEach(() => vi.clearAllMocks());

  it('executes replay-all and shows success toast with count', async () => {
    vi.mocked(rulesApi.replayAll).mockResolvedValueOnce({
      replayed: 5, totalMatched: 5, failed: 0
    } as any);

    const { result } = renderHook(() => useReplayAll(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate(1);
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(toast.success).toHaveBeenCalledWith(
      expect.stringContaining('Replayed 5 of 5')
    );
  });

  it('calls replayAll API with the ruleId when no messages matched', async () => {
    // The hook uses toast() (info variant) for this case - just verify API is called
    vi.mocked(rulesApi.replayAll).mockResolvedValueOnce({
      replayed: 0, totalMatched: 0, failed: 0
    } as any);

    const { result } = renderHook(() => useReplayAll(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate(1);
    });

    await waitFor(() => expect(rulesApi.replayAll).toHaveBeenCalledWith(1));
  });

  it('shows error toast when all fail', async () => {
    vi.mocked(rulesApi.replayAll).mockResolvedValueOnce({
      replayed: 0, totalMatched: 3, failed: 3
    } as any);

    const { result } = renderHook(() => useReplayAll(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate(1);
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(toast.error).toHaveBeenCalledWith(
      expect.stringContaining('3 matched messages failed')
    );
  });

  it('shows error toast on API failure', async () => {
    vi.mocked(rulesApi.replayAll).mockRejectedValueOnce(new Error('fail'));

    const { result } = renderHook(() => useReplayAll(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate(1);
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(toast.error).toHaveBeenCalledWith('Failed to execute replay-all');
  });
});

// ─── useNamespaces ───────────────────────────────────────────────────────────

describe('useNamespaces', () => {
  beforeEach(() => vi.clearAllMocks());

  it('fetches namespaces list', async () => {
    const mockNs = [{ id: 'ns-1', name: 'MyNamespace' }];
    vi.mocked(namespacesApi.list).mockResolvedValueOnce(mockNs as any);

    const { result } = renderHook(() => useNamespaces(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(mockNs);
  });
});

// ─── useNamespace ────────────────────────────────────────────────────────────

describe('useNamespace', () => {
  beforeEach(() => vi.clearAllMocks());

  it('fetches namespace by id', async () => {
    const mockNs = { id: 'ns-1', name: 'Namespace 1' };
    vi.mocked(namespacesApi.get).mockResolvedValueOnce(mockNs as any);

    const { result } = renderHook(() => useNamespace('ns-1'), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual(mockNs);
  });

  it('is disabled when id is empty', () => {
    const { result } = renderHook(() => useNamespace(''), { wrapper: createWrapper() });

    expect(result.current.fetchStatus).toBe('idle');
    expect(namespacesApi.get).not.toHaveBeenCalled();
  });
});

// ─── useCreateNamespace ──────────────────────────────────────────────────────

describe('useCreateNamespace', () => {
  beforeEach(() => vi.clearAllMocks());

  it('creates namespace and shows success toast', async () => {
    const newNs = { id: 'ns-new', name: 'New NS' };
    vi.mocked(namespacesApi.create).mockResolvedValueOnce(newNs as any);

    const { result } = renderHook(() => useCreateNamespace(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({ name: 'New NS', connectionString: 'sb://...' } as any);
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(toast.success).toHaveBeenCalledWith('Namespace connected successfully');
  });

  it('shows detailed error toast on failure with API message', async () => {
    const error = { response: { data: { detail: 'Invalid connection string' } } };
    vi.mocked(namespacesApi.create).mockRejectedValueOnce(error);

    const { result } = renderHook(() => useCreateNamespace(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({ name: 'Bad NS', connectionString: 'invalid' } as any);
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(toast.error).toHaveBeenCalledWith('Invalid connection string', expect.any(Object));
  });

  it('shows fallback error toast when no detail available', async () => {
    vi.mocked(namespacesApi.create).mockRejectedValueOnce(new Error('Network error'));

    const { result } = renderHook(() => useCreateNamespace(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({ name: 'Bad NS', connectionString: 'invalid' } as any);
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(toast.error).toHaveBeenCalledWith('Network error', expect.any(Object));
  });
});

// ─── useDeleteNamespace ──────────────────────────────────────────────────────

describe('useDeleteNamespace', () => {
  beforeEach(() => vi.clearAllMocks());

  it('deletes namespace and shows success toast', async () => {
    vi.mocked(namespacesApi.delete).mockResolvedValueOnce(undefined as any);

    const { result } = renderHook(() => useDeleteNamespace(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate('ns-1');
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(toast.success).toHaveBeenCalledWith('Namespace deleted');
  });

  it('shows error toast on failure', async () => {
    vi.mocked(namespacesApi.delete).mockRejectedValueOnce(new Error('fail'));

    const { result } = renderHook(() => useDeleteNamespace(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate('ns-1');
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(toast.error).toHaveBeenCalledWith(
      expect.stringContaining('Failed to delete namespace'),
      expect.any(Object)
    );
  });
});

// ─── useTestConnection ───────────────────────────────────────────────────────

describe('useTestConnection', () => {
  beforeEach(() => vi.clearAllMocks());

  it('shows success toast when connected', async () => {
    vi.mocked(namespacesApi.testConnection).mockResolvedValueOnce({
      isConnected: true, message: 'Connection successful'
    } as any);

    const { result } = renderHook(() => useTestConnection(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate('ns-1');
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(toast.success).toHaveBeenCalledWith('Connection successful');
  });

  it('shows error toast when not connected', async () => {
    vi.mocked(namespacesApi.testConnection).mockResolvedValueOnce({
      isConnected: false, message: 'Cannot reach namespace'
    } as any);

    const { result } = renderHook(() => useTestConnection(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate('ns-1');
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(toast.error).toHaveBeenCalledWith('Cannot reach namespace', expect.any(Object));
  });

  it('shows fallback toast when message is empty', async () => {
    vi.mocked(namespacesApi.testConnection).mockResolvedValueOnce({
      isConnected: false, message: undefined
    } as any);

    const { result } = renderHook(() => useTestConnection(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate('ns-1');
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(toast.error).toHaveBeenCalledWith(
      expect.stringContaining('Connection failed'),
      expect.any(Object)
    );
  });

  it('shows error toast on API failure', async () => {
    vi.mocked(namespacesApi.testConnection).mockRejectedValueOnce(new Error('fail'));

    const { result } = renderHook(() => useTestConnection(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate('ns-1');
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(toast.error).toHaveBeenCalledWith(
      expect.stringContaining('Failed to test connection'),
      expect.any(Object)
    );
  });
});
