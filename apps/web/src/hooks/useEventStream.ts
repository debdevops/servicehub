import { useEffect, useState } from 'react';
import { useQueryClient, type QueryKey } from '@tanstack/react-query';
import { connectEventStream, type PlatformEventFrame } from '@/lib/api/eventStream';
import { useDemoContext } from '@/lib/demo/DemoContext';

const INVALIDATE_DEBOUNCE_MS = 2_000;

function queryKeysForEvent(event: PlatformEventFrame): QueryKey[] {
  const keys: QueryKey[] = [];
  if (event.category === 'namespace') {
    keys.push(['namespaces']);
  }
  if (event.namespaceId) {
    keys.push(
      ['queues', event.namespaceId],
      ['namespace-stats', event.namespaceId],
      ['dlq-trend', event.namespaceId],
    );
  }
  return keys;
}

/**
 * Subscribes to the platform event stream (SSE) and translates incoming events
 * into TanStack Query invalidations, so dashboard data refreshes the moment the
 * backend detects a DLQ spike or a namespace changes — without waiting for the
 * next poll tick.
 *
 * Delivery is best-effort (in-process bus, drop-oldest buffers, reconnect gaps),
 * so callers must keep polling as a safety net; `connected` tells them when they
 * can relax the poll cadence. Disabled in demo mode.
 */
export function useEventStream(enabled = true): { connected: boolean } {
  const queryClient = useQueryClient();
  const { isDemoMode } = useDemoContext();
  const [connected, setConnected] = useState(false);
  const active = enabled && !isDemoMode;

  useEffect(() => {
    if (!active) return;

    // Coalesce bursts (e.g. a DLQ flood) into one invalidation per key per window.
    const pendingKeys = new Map<string, QueryKey>();
    let flushTimer: ReturnType<typeof setTimeout> | null = null;

    const flush = () => {
      flushTimer = null;
      for (const key of pendingKeys.values()) {
        void queryClient.invalidateQueries({ queryKey: key });
      }
      pendingKeys.clear();
    };

    const disconnect = connectEventStream({
      onEvent: event => {
        for (const key of queryKeysForEvent(event)) {
          pendingKeys.set(JSON.stringify(key), key);
        }
        if (pendingKeys.size > 0 && flushTimer === null) {
          flushTimer = setTimeout(flush, INVALIDATE_DEBOUNCE_MS);
        }
      },
      onConnectionChange: setConnected,
    });

    return () => {
      disconnect();
      if (flushTimer !== null) clearTimeout(flushTimer);
      setConnected(false);
    };
  }, [active, queryClient]);

  return { connected };
}
