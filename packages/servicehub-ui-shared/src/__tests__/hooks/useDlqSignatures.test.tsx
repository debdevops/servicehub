import { vi, describe, it, expect, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';

vi.mock('../../lib/api/dlqSignatures', () => ({
  dlqSignaturesApi: {
    getSignatures: vi.fn(),
  },
}));

import { dlqSignaturesApi, type DlqSignaturesResponse } from '../../lib/api/dlqSignatures';
import { useDlqSignatures } from '../../hooks/useDlqSignatures';

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
