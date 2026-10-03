import { vi, describe, it, expect, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';

vi.mock('../../lib/api/recovery', () => ({
  recoveryApi: {
    getAgeing: vi.fn(),
  },
}));

import { recoveryApi } from '../../lib/api/recovery';
import { useRecoveryAgeing } from '../../hooks/useRecoveryAgeing';

const mockGetAgeing = recoveryApi.getAgeing as ReturnType<typeof vi.fn>;

function createWrapper() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(QueryClientProvider, { client: queryClient }, children);
  };
}

describe('useRecoveryAgeing', () => {
  beforeEach(() => vi.clearAllMocks());

  it('returns the open (non-terminal) entries', async () => {
    mockGetAgeing.mockResolvedValueOnce([{ id: 'entry-1', state: 'Observing' }]);

    const { result } = renderHook(() => useRecoveryAgeing(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toHaveLength(1);
    expect(mockGetAgeing).toHaveBeenCalled();
  });
});
