import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useLiveTail } from '../../hooks/useLiveTail';
import { connectLiveTail, type LiveTailOptions } from '../../lib/api/liveTail';
import type { Message } from '../../lib/api/types';

vi.mock('../../lib/api/liveTail', () => ({
  connectLiveTail: vi.fn(),
}));

function buildMessage(overrides: Partial<Message> = {}): Message {
  return {
    messageId: 'm1',
    sequenceNumber: 1,
    enqueuedTime: '2026-07-08T00:00:00Z',
    deliveryCount: 1,
    state: 'Active',
    contentType: 'application/json',
    body: '{}',
    ...overrides,
  };
}

describe('useLiveTail', () => {
  const disconnectSpy = vi.fn();
  let capturedOptions: LiveTailOptions | null = null;

  beforeEach(() => {
    capturedOptions = null;
    disconnectSpy.mockClear();
    vi.mocked(connectLiveTail).mockClear();
    vi.mocked(connectLiveTail).mockImplementation(options => {
      capturedOptions = options;
      return disconnectSpy;
    });
  });

  it('starts idle and does not connect until start() is called', () => {
    const { result } = renderHook(() => useLiveTail({ namespaceId: 'ns-1', entityName: 'orders' }));

    expect(result.current.status).toBe('idle');
    expect(connectLiveTail).not.toHaveBeenCalled();
  });

  it('start() connects and reports connecting then connected', () => {
    const { result } = renderHook(() => useLiveTail({ namespaceId: 'ns-1', entityName: 'orders' }));

    act(() => result.current.start());
    expect(result.current.status).toBe('connecting');
    expect(connectLiveTail).toHaveBeenCalledWith(
      expect.objectContaining({ namespaceId: 'ns-1', entityName: 'orders' }),
    );

    act(() => capturedOptions!.onConnectionChange!(true));
    expect(result.current.status).toBe('connected');
  });

  it('start() is a no-op while already running', () => {
    const { result } = renderHook(() => useLiveTail({ namespaceId: 'ns-1', entityName: 'orders' }));

    act(() => result.current.start());
    act(() => result.current.start());

    expect(connectLiveTail).toHaveBeenCalledTimes(1);
  });

  it('accumulates newly-arrived messages, newest first', () => {
    const { result } = renderHook(() => useLiveTail({ namespaceId: 'ns-1', entityName: 'orders' }));

    act(() => result.current.start());
    act(() => capturedOptions!.onMessage(buildMessage({ messageId: 'm1' })));
    act(() => capturedOptions!.onMessage(buildMessage({ messageId: 'm2' })));

    expect(result.current.messages.map(m => m.messageId)).toEqual(['m2', 'm1']);
  });

  it('caps the buffered message list at 200', () => {
    const { result } = renderHook(() => useLiveTail({ namespaceId: 'ns-1', entityName: 'orders' }));

    act(() => result.current.start());
    act(() => {
      for (let i = 0; i < 250; i++) {
        capturedOptions!.onMessage(buildMessage({ messageId: `m${i}` }));
      }
    });

    expect(result.current.messages).toHaveLength(200);
    expect(result.current.messages[0].messageId).toBe('m249');
  });

  it('clear() empties the message buffer without disconnecting', () => {
    const { result } = renderHook(() => useLiveTail({ namespaceId: 'ns-1', entityName: 'orders' }));

    act(() => result.current.start());
    act(() => capturedOptions!.onMessage(buildMessage()));
    expect(result.current.messages).toHaveLength(1);

    act(() => result.current.clear());

    expect(result.current.messages).toHaveLength(0);
    expect(disconnectSpy).not.toHaveBeenCalled();
  });

  it('stop() disconnects and resets status to idle', () => {
    const { result } = renderHook(() => useLiveTail({ namespaceId: 'ns-1', entityName: 'orders' }));

    act(() => result.current.start());
    act(() => result.current.stop());

    expect(disconnectSpy).toHaveBeenCalledTimes(1);
    expect(result.current.status).toBe('idle');
  });

  it('reports unsupported status and does not get overwritten by a later disconnect', () => {
    const { result } = renderHook(() => useLiveTail({ namespaceId: 'ns-1', entityName: 'checkout-queue' }));

    act(() => result.current.start());
    act(() => capturedOptions!.onUnsupported!());
    expect(result.current.status).toBe('unsupported');

    act(() => capturedOptions!.onConnectionChange!(false));
    expect(result.current.status).toBe('unsupported');
  });

  it('onSessionExpired stops the session', () => {
    const { result } = renderHook(() => useLiveTail({ namespaceId: 'ns-1', entityName: 'orders' }));

    act(() => result.current.start());
    act(() => capturedOptions!.onSessionExpired!());

    expect(disconnectSpy).toHaveBeenCalledTimes(1);
    expect(result.current.status).toBe('idle');
  });

  it('disconnects on unmount', () => {
    const { result, unmount } = renderHook(() => useLiveTail({ namespaceId: 'ns-1', entityName: 'orders' }));

    act(() => result.current.start());
    unmount();

    expect(disconnectSpy).toHaveBeenCalledTimes(1);
  });
});
