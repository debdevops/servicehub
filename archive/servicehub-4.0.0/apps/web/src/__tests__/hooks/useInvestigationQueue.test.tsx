import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

vi.mock('@servicehub/ui-shared/lib/api/client', () => ({
  apiClient: {
    get: vi.fn().mockResolvedValue({
      data: {
        metrics: {
          totalSignatures: 0,
          activeSignatures: 0,
          resolvedSignatures: 0,
          suppressedSignatures: 0,
          archivedSignatures: 0,
          requiresAction: 0,
        },
        investigationQueue: [],
        failedReplays: [],
        knowledgeReview: [],
        newSignatures: [],
        recentlyChanged: [],
        fleetHealth: null,
      },
    }),
  },
}));

import { apiClient } from '@servicehub/ui-shared/lib/api/client';
import { useInvestigationQueue } from '@servicehub/ui-shared/hooks/useInvestigationQueue';

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
}

describe('useInvestigationQueue', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('requests the investigation-center path without a duplicated /api/v1 prefix', async () => {
    const { result } = renderHook(() => useInvestigationQueue(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    // Regression: this call used to hardcode a leading '/api/v1/...' on top of the
    // apiClient baseURL (already '/api/v1'), producing '/api/v1/api/v1/...' — a 404
    // that repeated on every 60s poll of the /incidents page.
    expect(apiClient.get).toHaveBeenCalledWith('/failure-intelligence/investigation-center');
    expect(apiClient.get).not.toHaveBeenCalledWith(
      expect.stringContaining('/api/v1/api/v1')
    );
  });
});
