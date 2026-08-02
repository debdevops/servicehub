import { vi, describe, it, expect, beforeEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';

vi.mock('../../lib/api/dlqSignatures', () => ({
  dlqSignaturesApi: {
    getSignatures: vi.fn(),
    getSignatureDetail: vi.fn(),
    getSignatureTimeline: vi.fn(),
    updateSignatureStatus: vi.fn(),
  },
}));

vi.mock('react-hot-toast', () => ({
  default: { success: vi.fn(), error: vi.fn() },
}));

import {
  dlqSignaturesApi,
  type DlqSignaturesResponse,
  type DlqSignatureDetail,
  type SignatureTimelineResponse,
} from '../../lib/api/dlqSignatures';
import toast from 'react-hot-toast';
import {
  useDlqSignatures,
  useDlqSignatureDetail,
  useSignatureTimeline,
  useResolveSignature,
  useReopenSignature,
  useSuppressSignature,
  useArchiveSignature,
} from '../../hooks/useDlqSignatures';

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(QueryClientProvider, { client: queryClient }, children);
  };
}

describe('useDlqSignatures', () => {
  beforeEach(() => vi.clearAllMocks());

  it('is disabled when namespaceId is undefined', () => {
    const { result } = renderHook(() => useDlqSignatures(undefined), { wrapper: createWrapper() });
    expect(result.current.loading).toBe(false);
    expect(dlqSignaturesApi.getSignatures).not.toHaveBeenCalled();
    expect(result.current.available).toBe(false);
  });

  it('surfaces available:true data as a normal loaded state', async () => {
    const response: DlqSignaturesResponse = {
      available: true,
      method: 'clustered',
      batchSize: 5,
      clusters: [
        {
          size: 4,
          messageIds: [1, 2, 3, 4],
          dominantEntity: 'orders-queue',
          dominantDeadletterReason: 'MaxDeliveryCountExceeded',
          dominantDeadletterReasonCount: 4,
          topTerms: ['timeout'],
          isNew: true,
          firstSeenAt: '2026-01-01T00:00:00Z',
          occurrenceCount: 1,
          windowStart: '2026-01-01T00:00:00Z',
          windowEnd: '2026-01-01T01:00:00Z',
          explanation: '4 messages: max delivery count exceeded on orders-queue.',
          signatureHash: 'hash-1',
          status: 'Active',
          trend: 'New',
        },
      ],
      singletons: [{ messageId: 5, dominantEntity: 'orders-queue', dominantDeadletterReason: 'TTLExpiredException' }],
    };
    vi.mocked(dlqSignaturesApi.getSignatures).mockResolvedValueOnce(response);

    const { result } = renderHook(() => useDlqSignatures('ns-1'), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(dlqSignaturesApi.getSignatures).toHaveBeenCalledWith('ns-1');
    expect(result.current.available).toBe(true);
    expect(result.current.data).toEqual(response);
    expect(result.current.error).toBeNull();
  });

  it('treats available:false as a normal state, not an error', async () => {
    const response: DlqSignaturesResponse = {
      available: false,
      method: null,
      batchSize: 5,
      clusters: [],
      singletons: [],
    };
    vi.mocked(dlqSignaturesApi.getSignatures).mockResolvedValueOnce(response);

    const { result } = renderHook(() => useDlqSignatures('ns-1'), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(result.current.available).toBe(false);
    expect(result.current.error).toBeNull();
    expect(result.current.data).toEqual(response);
  });

  it('surfaces a real request failure as an error, distinct from available:false', async () => {
    vi.mocked(dlqSignaturesApi.getSignatures).mockRejectedValueOnce({ response: { status: 404 } });

    const { result } = renderHook(() => useDlqSignatures('ns-1'), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.error).not.toBeNull());
    expect(result.current.available).toBe(false);
  });
});

// ─── useDlqSignatureDetail ────────────────────────────────────────────────────

describe('useDlqSignatureDetail', () => {
  beforeEach(() => vi.clearAllMocks());

  it('is disabled when namespaceId or signatureHash is missing', () => {
    const { result } = renderHook(() => useDlqSignatureDetail(undefined, 'hash-1'), { wrapper: createWrapper() });
    expect(result.current.fetchStatus).toBe('idle');
    expect(dlqSignaturesApi.getSignatureDetail).not.toHaveBeenCalled();
  });

  it('calls getSignatureDetail with namespaceId and signatureHash', async () => {
    const detail: DlqSignatureDetail = {
      signatureHash: 'hash-1',
      namespaceId: 'ns-1',
      size: 4,
      messageIds: [1, 2, 3, 4],
      dominantEntity: 'orders-queue',
      dominantDeadletterReason: 'MaxDeliveryCountExceeded',
      dominantDeadletterReasonCount: 4,
      topTerms: ['timeout'],
      isNew: false,
      firstSeenAt: '2026-01-01T00:00:00Z',
      occurrenceCount: 4,
      windowStart: '2026-01-01T00:00:00Z',
      windowEnd: '2026-01-01T01:00:00Z',
      explanation: 'explanation',
      status: 'Active',
      trend: 'Recurring',
      confidence: 'High',
      isCurrentlyClustered: true,
      relatedMessages: [],
    };
    vi.mocked(dlqSignaturesApi.getSignatureDetail).mockResolvedValueOnce(detail);

    const { result } = renderHook(() => useDlqSignatureDetail('ns-1', 'hash-1'), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(dlqSignaturesApi.getSignatureDetail).toHaveBeenCalledWith('ns-1', 'hash-1');
    expect(result.current.data).toEqual(detail);
  });
});

// ─── useSignatureTimeline ─────────────────────────────────────────────────────

describe('useSignatureTimeline', () => {
  beforeEach(() => vi.clearAllMocks());

  it('is disabled when namespaceId or signatureHash is missing', () => {
    const { result } = renderHook(() => useSignatureTimeline('ns-1', undefined), { wrapper: createWrapper() });
    expect(result.current.fetchStatus).toBe('idle');
    expect(dlqSignaturesApi.getSignatureTimeline).not.toHaveBeenCalled();
  });

  it('calls getSignatureTimeline with namespaceId and signatureHash', async () => {
    const timeline: SignatureTimelineResponse = {
      signatureHash: 'hash-1',
      events: [{ eventType: 'SignatureFirstObserved', description: 'first', timestamp: '2026-01-01T00:00:00Z', details: null }],
    };
    vi.mocked(dlqSignaturesApi.getSignatureTimeline).mockResolvedValueOnce(timeline);

    const { result } = renderHook(() => useSignatureTimeline('ns-1', 'hash-1'), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(dlqSignaturesApi.getSignatureTimeline).toHaveBeenCalledWith('ns-1', 'hash-1');
    expect(result.current.data).toEqual(timeline);
  });
});

// ─── Lifecycle mutation hooks ─────────────────────────────────────────────────

describe('signature lifecycle mutation hooks', () => {
  beforeEach(() => vi.clearAllMocks());

  const cases = [
    { name: 'useResolveSignature', hook: useResolveSignature, target: 'Resolved' as const },
    { name: 'useReopenSignature', hook: useReopenSignature, target: 'Reopened' as const },
    { name: 'useSuppressSignature', hook: useSuppressSignature, target: 'Suppressed' as const },
    { name: 'useArchiveSignature', hook: useArchiveSignature, target: 'Archived' as const },
  ];

  for (const { name, hook, target } of cases) {
    it(`${name}: calls updateSignatureStatus with '${target}' and shows a success toast`, async () => {
      vi.mocked(dlqSignaturesApi.updateSignatureStatus).mockResolvedValueOnce({
        signatureHash: 'hash-1',
        status: target,
        previousStatus: 'Active',
        transitionedAt: '2026-01-02T00:00:00Z',
        notes: null,
      });

      const { result } = renderHook(() => hook(), { wrapper: createWrapper() });

      await act(async () => {
        result.current.mutate({ namespaceId: 'ns-1', signatureHash: 'hash-1', notes: 'note' });
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(dlqSignaturesApi.updateSignatureStatus).toHaveBeenCalledWith('ns-1', 'hash-1', target, 'note');
      expect(toast.success).toHaveBeenCalledWith(`Signature marked ${target.toLowerCase()}`);
    });
  }

  it('shows API error detail on failure', async () => {
    vi.mocked(dlqSignaturesApi.updateSignatureStatus).mockRejectedValueOnce({
      response: { data: { detail: 'Cannot transition from Archived to Resolved.' } },
    });

    const { result } = renderHook(() => useResolveSignature(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({ namespaceId: 'ns-1', signatureHash: 'hash-1' });
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(toast.error).toHaveBeenCalledWith('Cannot transition from Archived to Resolved.');
  });
});
