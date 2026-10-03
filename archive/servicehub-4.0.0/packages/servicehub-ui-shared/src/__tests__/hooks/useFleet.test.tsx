import { vi, describe, it, expect, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';

vi.mock('../../lib/api/fleet', () => ({
  fleetApi: {
    getOverview: vi.fn(),
  },
}));

import { fleetApi } from '../../lib/api/fleet';
import { useFleetOverview } from '../../hooks/useFleet';

const mockGetOverview = fleetApi.getOverview as ReturnType<typeof vi.fn>;

function createWrapper() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(QueryClientProvider, { client: queryClient }, children);
  };
}

describe('useFleetOverview', () => {
  beforeEach(() => vi.clearAllMocks());

  it('returns overview data on success', async () => {
    mockGetOverview.mockResolvedValueOnce({ totalActive: 7, namespaces: [], windowHours: 24 });

    const { result } = renderHook(() => useFleetOverview(24), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.totalActive).toBe(7);
    expect(mockGetOverview).toHaveBeenCalledWith(24);
  });

  it('does not fetch when disabled', () => {
    mockGetOverview.mockReturnValue(new Promise(() => {}));

    renderHook(() => useFleetOverview(24, false), { wrapper: createWrapper() });

    expect(mockGetOverview).not.toHaveBeenCalled();
  });
});
