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
    upsertKnowledge: vi.fn(),
    getKnowledgeHistory: vi.fn(),
    markForReview: vi.fn(),
    getRootCauseMatches: vi.fn(),
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
  type RootCauseExplorerResponse,
} from '../../lib/api/dlqSignatures';
import toast from 'react-hot-toast';
import {
  useDlqSignatures,
  useDlqSignatureDetail,
  useSignatureTimeline,
  useKnowledgeHistory,
  useResolveSignature,
  useReopenSignature,
  useSuppressSignature,
  useArchiveSignature,
  useUpsertKnowledge,
  useMarkForReview,
  useRootCauseMatches,
} from '../../hooks/useDlqSignatures';
import { DemoModeProvider } from '../../lib/demo/DemoContext';

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(QueryClientProvider, { client: queryClient }, children);
  };
}

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

// ─── useRootCauseMatches ──────────────────────────────────────────────────────

describe('useRootCauseMatches', () => {
  beforeEach(() => vi.clearAllMocks());

  it('is disabled when namespaceId or signatureHash is missing', () => {
    const { result } = renderHook(() => useRootCauseMatches(undefined, 'hash-1'), { wrapper: createWrapper() });
    expect(result.current.fetchStatus).toBe('idle');
    expect(dlqSignaturesApi.getRootCauseMatches).not.toHaveBeenCalled();
  });

  it('calls getRootCauseMatches with namespaceId and signatureHash', async () => {
    const response: RootCauseExplorerResponse = {
      signatureHash: 'hash-1',
      dominantDeadletterReason: 'MaxDeliveryCountExceeded',
      topTerms: ['timeout'],
      totalOccurrencesAcrossFleet: 9,
      matches: [
        {
          namespaceId: 'ns-2',
          occurrenceCount: 5,
          firstSeenAt: '2026-01-01T00:00:00Z',
          lastSeenAt: '2026-01-02T00:00:00Z',
          lifecycleStatus: 'Resolved',
          knowledge: null,
        },
      ],
    };
    vi.mocked(dlqSignaturesApi.getRootCauseMatches).mockResolvedValueOnce(response);

    const { result } = renderHook(() => useRootCauseMatches('ns-1', 'hash-1'), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(dlqSignaturesApi.getRootCauseMatches).toHaveBeenCalledWith('ns-1', 'hash-1');
    expect(result.current.data).toEqual(response);
  });

  it('demo mode: returns curated fixture data without calling the real API', async () => {
    const { result } = renderHook(
      () => useRootCauseMatches('demo-gcp-medstream-prod', 'demo-deserialization-failure'),
      { wrapper: createDemoWrapper('gcp') },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(dlqSignaturesApi.getRootCauseMatches).not.toHaveBeenCalled();
    expect(result.current.data?.matches.length).toBeGreaterThan(0);
    expect(result.current.data?.matches[0].knowledge?.rootCause).toBeTruthy();
  });

  it('demo mode: a signature with no fleet matches returns an empty matches array', async () => {
    const { result } = renderHook(
      () => useRootCauseMatches('demo-azure-contoso-prod', 'demo-max-delivery-count-exceeded'),
      { wrapper: createDemoWrapper('azure') },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.matches).toEqual([]);
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

// ─── Demo Mode fixtures ────────────────────────────────────────────────────

describe('demo mode data fixtures', () => {
  beforeEach(() => vi.clearAllMocks());

  it('useDlqSignatures returns curated demo clusters without calling the real API', async () => {
    const { result } = renderHook(() => useDlqSignatures('demo-azure-contoso-prod'), {
      wrapper: createDemoWrapper(),
    });

    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(dlqSignaturesApi.getSignatures).not.toHaveBeenCalled();
    expect(result.current.available).toBe(true);
    expect(result.current.data?.clusters.length).toBeGreaterThan(0);
    expect(result.current.data?.clusters[0].knowledge).toBeTruthy();
  });

  it('useDlqSignatureDetail returns curated demo detail for a known demo hash', async () => {
    const { result } = renderHook(
      () => useDlqSignatureDetail('demo-azure-contoso-prod', 'demo-max-delivery-count-exceeded'),
      { wrapper: createDemoWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(dlqSignaturesApi.getSignatureDetail).not.toHaveBeenCalled();
    expect(result.current.data?.signatureHash).toBe('demo-max-delivery-count-exceeded');
    expect(result.current.data?.relatedMessages).toEqual([]);
  });

  it('useSignatureTimeline returns a curated demo timeline for a known demo hash', async () => {
    const { result } = renderHook(
      () => useSignatureTimeline('demo-azure-contoso-prod', 'demo-max-delivery-count-exceeded'),
      { wrapper: createDemoWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(dlqSignaturesApi.getSignatureTimeline).not.toHaveBeenCalled();
    expect(result.current.data?.events.length).toBeGreaterThan(0);
  });
});

// ─── useKnowledgeHistory ────────────────────────────────────────────────────

describe('useKnowledgeHistory', () => {
  beforeEach(() => vi.clearAllMocks());

  it('is disabled when namespaceId or signatureHash is missing', () => {
    const { result } = renderHook(() => useKnowledgeHistory(undefined, 'hash-1'), { wrapper: createWrapper() });
    expect(result.current.fetchStatus).toBe('idle');
    expect(dlqSignaturesApi.getKnowledgeHistory).not.toHaveBeenCalled();
  });

  it('calls getKnowledgeHistory with namespaceId and signatureHash', async () => {
    vi.mocked(dlqSignaturesApi.getKnowledgeHistory).mockResolvedValueOnce([
      {
        knowledgeVersion: 1,
        rootCause: 'v1',
        resolutionNotes: null,
        operationalNotes: null,
        runbookLink: null,
        owner: null,
        replayGuidance: null,
        tags: null,
        reviewDueAt: null,
        updatedBy: null,
        updatedAt: '2026-01-01T00:00:00Z',
      },
    ]);

    const { result } = renderHook(() => useKnowledgeHistory('ns-1', 'hash-1'), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(dlqSignaturesApi.getKnowledgeHistory).toHaveBeenCalledWith('ns-1', 'hash-1');
    expect(result.current.data).toHaveLength(1);
  });
});

// ─── useUpsertKnowledge ─────────────────────────────────────────────────────

describe('useUpsertKnowledge', () => {
  beforeEach(() => vi.clearAllMocks());

  it('calls upsertKnowledge and shows a success toast', async () => {
    vi.mocked(dlqSignaturesApi.upsertKnowledge).mockResolvedValueOnce({
      rootCause: 'Timeout',
      resolutionNotes: null,
      operationalNotes: null,
      runbookLink: null,
      owner: null,
      replayGuidance: null,
      lastUpdatedAt: '2026-01-01T00:00:00Z',
      knowledgeVersion: 2,
      reviewDueAt: null,
      tags: null,
      updatedBy: 'alice@example.com',
      isReviewOverdue: false,
    });

    const { result } = renderHook(() => useUpsertKnowledge(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({
        namespaceId: 'ns-1',
        signatureHash: 'hash-1',
        request: { rootCause: 'Timeout', changedBy: 'alice@example.com' },
      });
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(dlqSignaturesApi.upsertKnowledge).toHaveBeenCalledWith('ns-1', 'hash-1', {
      rootCause: 'Timeout',
      changedBy: 'alice@example.com',
    });
    expect(toast.success).toHaveBeenCalledWith('Knowledge saved');
  });

  it('rejects without calling the API in demo mode', async () => {
    const { result } = renderHook(() => useUpsertKnowledge(), { wrapper: createDemoWrapper() });

    await act(async () => {
      result.current.mutate({ namespaceId: 'ns-1', signatureHash: 'hash-1', request: { rootCause: 'x' } });
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(dlqSignaturesApi.upsertKnowledge).not.toHaveBeenCalled();
  });

  it('shows API error detail on failure', async () => {
    vi.mocked(dlqSignaturesApi.upsertKnowledge).mockRejectedValueOnce({
      response: { data: { detail: 'Root cause is required.' } },
    });

    const { result } = renderHook(() => useUpsertKnowledge(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({ namespaceId: 'ns-1', signatureHash: 'hash-1', request: { rootCause: '' } });
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(toast.error).toHaveBeenCalledWith('Root cause is required.');
  });
});

// ─── useMarkForReview ───────────────────────────────────────────────────────

describe('useMarkForReview', () => {
  beforeEach(() => vi.clearAllMocks());

  it('calls markForReview and shows a success toast', async () => {
    vi.mocked(dlqSignaturesApi.markForReview).mockResolvedValueOnce({
      rootCause: null,
      resolutionNotes: null,
      operationalNotes: null,
      runbookLink: null,
      owner: null,
      replayGuidance: null,
      lastUpdatedAt: '2026-01-01T00:00:00Z',
      knowledgeVersion: 1,
      reviewDueAt: '2026-02-01T00:00:00Z',
      tags: null,
      updatedBy: null,
      isReviewOverdue: false,
    });

    const { result } = renderHook(() => useMarkForReview(), { wrapper: createWrapper() });

    await act(async () => {
      result.current.mutate({ namespaceId: 'ns-1', signatureHash: 'hash-1', reviewDueAt: '2026-02-01T00:00:00Z' });
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(dlqSignaturesApi.markForReview).toHaveBeenCalledWith('ns-1', 'hash-1', '2026-02-01T00:00:00Z');
    expect(toast.success).toHaveBeenCalledWith('Marked for review');
  });

  it('rejects without calling the API in demo mode', async () => {
    const { result } = renderHook(() => useMarkForReview(), { wrapper: createDemoWrapper() });

    await act(async () => {
      result.current.mutate({ namespaceId: 'ns-1', signatureHash: 'hash-1', reviewDueAt: '2026-02-01T00:00:00Z' });
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(dlqSignaturesApi.markForReview).not.toHaveBeenCalled();
  });
});
