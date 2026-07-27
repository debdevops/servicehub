import { useCallback, useEffect, useRef, useState } from 'react';
import { connectLiveTail, type LiveTailParams } from '@/lib/api/liveTail';
import type { Message } from '@/lib/api/types';
import { useDemoContext } from '@/lib/demo/DemoContext';

const MAX_BUFFERED_MESSAGES = 200;

export type LiveTailStatus = 'idle' | 'connecting' | 'connected' | 'disconnected' | 'unsupported';

/**
 * Manages a Live Tail viewing session for one queue/subscription: connects on `start()`,
 * accumulates newly-arrived messages (newest first, capped at {@link MAX_BUFFERED_MESSAGES}),
 * and disconnects on `stop()` or unmount. Purely opt-in — nothing connects until `start()`
 * is called, so idle pages never poll a cloud provider in the background.
 */
export function useLiveTail(params: LiveTailParams) {
  const [status, setStatus] = useState<LiveTailStatus>('idle');
  const [messages, setMessages] = useState<Message[]>([]);
  const disconnectRef = useRef<(() => void) | null>(null);
  const paramsRef = useRef(params);
  paramsRef.current = params;
  const { isDemoMode } = useDemoContext();

  const stop = useCallback(() => {
    disconnectRef.current?.();
    disconnectRef.current = null;
    setStatus('idle');
  }, []);

  // Demo namespaces have no real provider connection behind them — never open an SSE
  // stream against the backend while in Demo Mode.
  const start = useCallback(() => {
    if (disconnectRef.current) return; // already running
    if (isDemoMode) {
      setStatus('unsupported');
      return;
    }
    setStatus('connecting');
    setMessages([]);

    disconnectRef.current = connectLiveTail({
      ...paramsRef.current,
      onMessage: message => {
        setMessages(prev => [message, ...prev].slice(0, MAX_BUFFERED_MESSAGES));
      },
      onConnectionChange: connected => {
        setStatus(prev => (prev === 'unsupported' ? prev : connected ? 'connected' : 'disconnected'));
      },
      onSessionExpired: () => {
        stop();
      },
      onUnsupported: () => {
        setStatus('unsupported');
      },
    });
  }, [stop, isDemoMode]);

  const clear = useCallback(() => setMessages([]), []);

  // Always disconnect on unmount, regardless of how the session was started.
  useEffect(() => () => disconnectRef.current?.(), []);

  return { status, messages, start, stop, clear };
}
