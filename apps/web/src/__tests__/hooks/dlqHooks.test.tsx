import { vi, describe, it, expect, beforeEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';

vi.mock('@/lib/api/dlqHistory', () => ({
  dlqHistoryApi: {
    getHistory: vi.fn(),
    getById: vi.fn(),
    getTimeline: vi.fn(),
    getSummary: vi.fn(),
    updateNotes: vi.fn(),
    setStatus: vi.fn(),
    batchSetStatus: vi.fn(),
    getForensicResult: vi.fn(),
    runForensic: vi.fn(),
    runBatchForensic: vi.fn(),
    exportCsv: vi.fn(),
  },
}));

vi.mock('react-hot-toast', () => ({
  default: { success: vi.fn(), error: vi.fn() },
}));

import { dlqHistoryApi } from '@/lib/api/dlqHistory';
import toast from 'react-hot-toast';
import {
  useDlqMessageDetail,
  useDlqTimeline,
  useDlqSummary,
  useUpdateDlqNotes,
} from '@/hooks/useDlqHistory';

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(QueryClientProvider, { client: queryClient }, children);
  };
}

// ─── useDlqMessageDetail ──────────────────────────────────────────────────────

describe('useDlqMessageDetail', () => {
  beforeEach(() => vi.clearAllMocks());

  it('is disabled when id is null', () => {
    const { result } = renderHook(() => useDlqMessageDetail(null), { wrapper: createWrapper() });
    expect(result.current.fetchStatus).toBe('idle');
    expect(dlqHistoryApi.getById).not.toHaveBeenCalled();
  });

  it('calls getById with the id', async () => {
    const detail = { id: 42, messageId: 'msg-42' };
    vi.mocked(dlqHistoryApi.getById).mockResolvedValueOnce(detail as any);

    const { result } = renderHook(() => useDlqMessageDetail(42), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(dlqHistoryApi.getById).toHaveBeenCalledWith(42);
    expect(result.current.data).toEqual(detail);
  });

  it('handles error gracefully', async () => {
    vi.mocked(dlqHistoryApi.getById).mockRejectedValueOnce(new Error('Not found'));

    const { result } = renderHook(() => useDlqMessageDetail(99), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});

// ─── useDlqTimeline ───────────────────────────────────────────────────────────

describe('useDlqTimeline', () => {
  beforeEach(() => vi.clearAllMocks());

  it('is disabled when id is null', () => {
    const { result } = renderHook(() => useDlqTimeline(null), { wrapper: createWrapper() });
    expect(result.current.fetchStatus).toBe('idle');
    expect(dlqHistoryApi.getTimeline).not.toHaveBeenCalled();
  });

  it('calls getTimeline with the id', async () => {
    const timeline = { events: [{ eventType: 'Enqueued', timestamp: '2024-01-01T00:00:00Z' }] };
    vi.mocked(dlqHistoryApi.getTimeline).mockResolvedValueOnce(timeline as any);

    const { result } = renderHook(() => useDlqTimeline(5), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(dlqHistoryApi.getTimeline).toHaveBeenCalledWith(5);
    expect(result.current.data).toEqual(timeline);
  });
});

// ─── useDlqSummary ────────────────────────────────────────────────────────────

describe('useDlqSummary', () => {
  beforeEach(() => vi.clearAllMocks());

  it('is disabled when namespaceId is undefined', () => {
    const { result } = renderHook(() => useDlqSummary(undefined), { wrapper: createWrapper() });
    expect(result.current.fetchStatus).toBe('idle');
    expect(dlqHistoryApi.getSummary).not.toHaveBeenCalled();
  });

  it('is disabled when namespaceId is empty string', () => {
    const { result } = renderHook(() => useDlqSummary(''), { wrapper: createWrapper() });
    expect(result.current.fetchStatus).toBe('idle');
  });

  it('calls getSummary with namespaceId', async () => {
    const summary = { activeMessages: 5, replayedMessages: 2, totalMessages: 7 };
    vi.mocked(dlqHistoryApi.getSummary).mockResolvedValueOnce(summary as any);

    const { result } = renderHook(() => useDlqSummary('ns-1'), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(dlqHistoryApi.getSummary).toHaveBeenCalledWith('ns-1');
    expect(result.current.data).toEqual(summary);
  });

  it('handles error gracefully', async () => {
    vi.mocked(dlqHistoryApi.getSummary).mockRejectedValueOnce({ response: { status: 404 } });

    const { result } = renderHook(() => useDlqSummary('ns-1'), { wrapper: createWrapper() });

    // 404 does not retry based on custom retry fn
    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});

// ─── useUpdateDlqNotes ────────────────────────────────────────────────────────

describe('useUpdateDlqNotes', () => {
  beforeEach(() => vi.clearAllMocks());

  it('updates notes and shows success toast', async () => {
    vi.mocked(dlqHistoryApi.updateNotes).mockResolvedValueOnce(undefined as any);

    const { result } = renderHook(() => useUpdateDlqNotes(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({ id: 10, notes: 'This is a test note' });
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(dlqHistoryApi.updateNotes).toHaveBeenCalledWith(10, 'This is a test note');
    expect(toast.success).toHaveBeenCalledWith('Notes updated successfully');
  });

  it('shows API error message on failure', async () => {
    vi.mocked(dlqHistoryApi.updateNotes).mockRejectedValueOnce({
      response: { data: { message: 'Note too long' } }
    });

    const { result } = renderHook(() => useUpdateDlqNotes(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({ id: 10, notes: 'x'.repeat(1000) });
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(toast.error).toHaveBeenCalledWith('Note too long');
  });

  it('shows generic error when no response message', async () => {
    vi.mocked(dlqHistoryApi.updateNotes).mockRejectedValueOnce(new Error('Network error'));

    const { result } = renderHook(() => useUpdateDlqNotes(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({ id: 10, notes: 'test' });
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(toast.error).toHaveBeenCalledWith('Network error');
  });

  it('shows fallback error message when error has no message', async () => {
    vi.mocked(dlqHistoryApi.updateNotes).mockRejectedValueOnce({});

    const { result } = renderHook(() => useUpdateDlqNotes(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({ id: 10, notes: 'test' });
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(toast.error).toHaveBeenCalledWith('Failed to update notes');
  });
});
