import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { useEventStream } from '@/hooks/useEventStream';
import { connectEventStream, type EventStreamOptions, type PlatformEventFrame } from '@/lib/api/eventStream';
import { DemoModeProvider } from '@/lib/demo/DemoContext';

vi.mock('@/lib/api/eventStream', () => ({
  connectEventStream: vi.fn(),
}));

function buildFrame(overrides: Partial<PlatformEventFrame> = {}): PlatformEventFrame {
  return {
    id: 'e1',
    eventType: 'servicehub.dlq.spike.detected.v1',
    category: 'dlq',
    severity: 'warning',
    occurredUtc: '2026-07-08T00:00:00Z',
    namespaceId: 'ns-1',
    ...overrides,
  };
}

describe('useEventStream', () => {
  const disconnectSpy = vi.fn();
  let capturedOptions: EventStreamOptions | null = null;
  let queryClient: QueryClient;
  let invalidateSpy: ReturnType<typeof vi.spyOn>;

  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );

  beforeEach(() => {
    vi.useFakeTimers();
    capturedOptions = null;
    disconnectSpy.mockClear();
    vi.mocked(connectEventStream).mockClear();
    vi.mocked(connectEventStream).mockImplementation(options => {
      capturedOptions = options;
      return disconnectSpy;
    });
    queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('reports connected=false initially and tracks connection changes', () => {
    const { result } = renderHook(() => useEventStream(), { wrapper });

    expect(result.current.connected).toBe(false);

    act(() => capturedOptions!.onConnectionChange!(true));
    expect(result.current.connected).toBe(true);

    act(() => capturedOptions!.onConnectionChange!(false));
    expect(result.current.connected).toBe(false);
  });

  it('invalidates namespace-scoped queries after the debounce window', () => {
    renderHook(() => useEventStream(), { wrapper });

    act(() => {
      capturedOptions!.onEvent(buildFrame());
      vi.advanceTimersByTime(2_000);
    });

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['queues', 'ns-1'] });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['namespace-stats', 'ns-1'] });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['dlq-trend', 'ns-1'] });
    expect(invalidateSpy).not.toHaveBeenCalledWith({ queryKey: ['namespaces'] });
  });

  it('invalidates the namespaces list for namespace-category events', () => {
    renderHook(() => useEventStream(), { wrapper });

    act(() => {
      capturedOptions!.onEvent(
        buildFrame({ category: 'namespace', eventType: 'servicehub.namespace.created.v1', namespaceId: null }),
      );
      vi.advanceTimersByTime(2_000);
    });

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['namespaces'] });
    expect(invalidateSpy).toHaveBeenCalledTimes(1);
  });

  it('coalesces an event burst into a single invalidation per query key', () => {
    renderHook(() => useEventStream(), { wrapper });

    act(() => {
      for (let i = 0; i < 25; i++) {
        capturedOptions!.onEvent(buildFrame({ id: `e${i}` }));
      }
      vi.advanceTimersByTime(2_000);
    });

    const queueCalls = invalidateSpy.mock.calls.filter(
      (call: unknown[]) => JSON.stringify((call[0] as { queryKey: unknown }).queryKey) === JSON.stringify(['queues', 'ns-1']),
    );
    expect(queueCalls).toHaveLength(1);
  });

  it('does not invalidate before the debounce window elapses', () => {
    renderHook(() => useEventStream(), { wrapper });

    act(() => {
      capturedOptions!.onEvent(buildFrame());
      vi.advanceTimersByTime(1_000);
    });

    expect(invalidateSpy).not.toHaveBeenCalled();
  });

  it('disconnects and resets state on unmount', () => {
    const { unmount } = renderHook(() => useEventStream(), { wrapper });

    unmount();

    expect(disconnectSpy).toHaveBeenCalledTimes(1);
  });

  it('does not connect when disabled', () => {
    renderHook(() => useEventStream(false), { wrapper });

    expect(connectEventStream).not.toHaveBeenCalled();
  });

  it('does not connect in demo mode', () => {
    const demoWrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={queryClient}>
        <DemoModeProvider cloudProvider="aws">{children}</DemoModeProvider>
      </QueryClientProvider>
    );

    renderHook(() => useEventStream(), { wrapper: demoWrapper });

    expect(connectEventStream).not.toHaveBeenCalled();
  });
});
