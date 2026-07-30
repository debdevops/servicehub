import { apiClient, getSpaToken, refreshSpaToken } from './client';
import type { Message } from './types';

export interface LiveTailParams {
  namespaceId: string;
  entityName: string;
  subscriptionName?: string;
  fromDeadLetter?: boolean;
}

export interface LiveTailOptions extends LiveTailParams {
  onMessage: (message: Message) => void;
  /** Fired with true once the stream is open, false when it drops. */
  onConnectionChange?: (connected: boolean) => void;
  /** Fired when the server closes the session after its max duration. */
  onSessionExpired?: () => void;
  /** Fired when the namespace's provider doesn't support repeatable peek (HTTP 409, e.g. AWS). */
  onUnsupported?: () => void;
}

/**
 * Connects to GET /api/v1/messages/live-tail and invokes a callback for each newly-arrived
 * message — "tail -f" for a single queue or subscription.
 *
 * Uses fetch + ReadableStream for the same reason {@link connectEventStream} does: the API
 * authenticates via the X-SPA-Token header, which native EventSource cannot send.
 *
 * Unlike the platform event stream, this does NOT auto-reconnect on drop — Live Tail is an
 * explicit, opt-in viewing session tied to one entity; leaving a background reconnect loop
 * running against a cloud provider after the user has moved on would be wasted API cost with
 * no one watching. A drop simply flips `connected` to false; the caller re-invokes
 * `connectLiveTail` to resume watching. The one exception is a 401 on the initial connect:
 * that's a stale token, not an abandoned session, so it gets a single retry with a refreshed
 * token before falling back to the same "flip to disconnected" behavior as any other drop.
 *
 * Returns a disconnect function.
 */
export function connectLiveTail(options: LiveTailOptions): () => void {
  const { namespaceId, entityName, subscriptionName, fromDeadLetter, onMessage, onConnectionChange, onSessionExpired, onUnsupported } = options;
  let active = true;
  let controller: AbortController | null = null;
  let retriedAfterAuthRefresh = false;

  const params = new URLSearchParams({ namespaceId, entityName });
  if (subscriptionName) params.set('subscriptionName', subscriptionName);
  if (fromDeadLetter) params.set('fromDeadLetter', 'true');
  const streamUrl = `${apiClient.defaults.baseURL ?? '/api/v1'}/messages/live-tail?${params.toString()}`;

  const run = async () => {
    if (!active) return;
    const runController = new AbortController();
    controller = runController;

    try {
      const token = getSpaToken();
      const response = await fetch(streamUrl, {
        headers: {
          Accept: 'text/event-stream',
          ...(token ? { 'X-SPA-Token': token } : {}),
        },
        cache: 'no-store',
        signal: runController.signal,
      });

      if (response.status === 401) {
        if (!retriedAfterAuthRefresh) {
          retriedAfterAuthRefresh = true;
          const refreshed = await refreshSpaToken();
          if (active && refreshed) {
            await run();
            return;
          }
        }
        if (active) {
          onConnectionChange?.(false);
        }
        return;
      }

      if (response.status === 409) {
        onUnsupported?.();
        return;
      }

      if (!response.ok || !response.body) {
        return;
      }

      onConnectionChange?.(true);

      const reader = response.body.getReader();
      const decoder = new TextDecoder();
      let buffer = '';

      for (;;) {
        const { done, value } = await reader.read();
        if (done) break;
        buffer += decoder.decode(value, { stream: true });

        let separatorIndex = buffer.indexOf('\n\n');
        while (separatorIndex !== -1) {
          const frame = buffer.slice(0, separatorIndex);
          buffer = buffer.slice(separatorIndex + 2);
          if (frame.trim() === ': session-expired') {
            onSessionExpired?.();
          } else {
            dispatchFrame(frame, onMessage);
          }
          separatorIndex = buffer.indexOf('\n\n');
        }
      }
    } catch {
      // Aborted by disconnect(), or the connection dropped — fall through, no auto-retry.
    }

    if (active) {
      onConnectionChange?.(false);
    }
  };

  void run();

  return () => {
    active = false;
    controller?.abort();
    onConnectionChange?.(false);
  };
}

function dispatchFrame(frame: string, onMessage: (message: Message) => void): void {
  const data = frame
    .split('\n')
    .filter(line => line.startsWith('data: '))
    .map(line => line.slice('data: '.length))
    .join('\n');

  if (!data) return;

  try {
    onMessage(JSON.parse(data) as Message);
  } catch {
    // Malformed frame — ignore rather than kill the stream.
  }
}
