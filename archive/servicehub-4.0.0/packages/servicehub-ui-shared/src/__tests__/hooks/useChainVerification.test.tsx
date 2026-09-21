import { vi, describe, it, expect, beforeEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';

vi.mock('react-hot-toast', () => ({
  default: { success: vi.fn(), error: vi.fn() },
}));

vi.mock('../../lib/api/recovery', () => ({
  recoveryApi: {
    verifyChain: vi.fn(),
  },
}));

import { recoveryApi } from '../../lib/api/recovery';
import { useVerifyChain } from '../../hooks/useChainVerification';
import { DemoModeProvider } from '../../lib/demo/DemoContext';

const mockVerifyChain = recoveryApi.verifyChain as ReturnType<typeof vi.fn>;

function createWrapper() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(QueryClientProvider, { client: queryClient }, children);
  };
}

function createDemoWrapper() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(
      QueryClientProvider,
      { client: queryClient },
      React.createElement(DemoModeProvider, { cloudProvider: 'azure' as const, children }),
    );
  };
}

describe('useVerifyChain', () => {
  beforeEach(() => vi.clearAllMocks());

  it('calls the real verify endpoint with the operation id', async () => {
    mockVerifyChain.mockResolvedValueOnce({
      ownerId: 'owner-1', isValid: true, eventsChecked: 10, firstDivergentSeq: null, reason: null,
    });

    const { result } = renderHook(() => useVerifyChain(), { wrapper: createWrapper() });

    await act(async () => {
      await result.current.mutateAsync('op-1');
    });

    expect(mockVerifyChain).toHaveBeenCalledWith('op-1');
  });

  it('never calls the backend in Demo Mode, and always reports the fixture chain as valid', async () => {
    const { result } = renderHook(() => useVerifyChain(), { wrapper: createDemoWrapper() });

    let outcome;
    await act(async () => {
      outcome = await result.current.mutateAsync('op-1');
    });

    expect(mockVerifyChain).not.toHaveBeenCalled();
    expect(outcome).toMatchObject({ isValid: true, firstDivergentSeq: null });
  });
});
