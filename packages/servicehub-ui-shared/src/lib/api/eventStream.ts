import { apiClient, getSpaToken, refreshSpaToken } from './client';

/**
 * Trimmed platform event pushed by GET /api/v1/events/stream.
 * Mirrors the backend EventsController.EventStreamItem projection.
 */
export interface PlatformEventFrame {
  id: string;
  eventType: string;
  category: string;
  severity: string;
  occurredUtc: string;
  cloudProvider?: string | null;
  namespaceId?: string | null;
  namespaceName?: string | null;
  correlationId?: string | null;
}

export interface EventStreamOptions {
  onEvent: (event: PlatformEventFrame) => void;
  /** Fired with true once the stream is open, false when it drops. */
  onConnectionChange?: (connected: boolean) => void;
}

const INITIAL_RETRY_MS = 1_000;
const MAX_RETRY_MS = 30_000;
// The server writes a heartbeat every 15s, so 45s of silence means the
// connection is dead even if the read never errors (half-open TCP after
// laptop sleep or a network switch never delivers an RST to the browser).
const STALL_TIMEOUT_MS = 45_000;

/**
 * Connects to the platform event stream and invokes callbacks for each event.
 *
 * Uses fetch + ReadableStream instead of native EventSource because the API
 * authenticates via the X-SPA-Token header, which EventSource cannot send
 * (and putting the token in the URL would leak it into proxy/access logs).
 * The trade-off is that reconnection is handled here: exponential backoff
 * from 1s to 30s, reset after a successful connection, with a token refresh
 * on 401 (same recovery path the axios interceptor uses).
 *
 * Returns a disconnect function; calling it stops all reconnection attempts.
 */
export function connectEventStream(options: EventStreamOptions): () => void {
  const { onEvent, onConnectionChange } = options;
  let active = true;
  let controller: AbortController | null = null;
  let retryTimer: ReturnType<typeof setTimeout> | null = null;
  let retryDelayMs = INITIAL_RETRY_MS;

  const streamUrl = `${apiClient.defaults.baseURL ?? '/api/v1'}/events/stream`;

  const scheduleReconnect = () => {
    if (!active) return;
    retryTimer = setTimeout(run, retryDelayMs);
    retryDelayMs = Math.min(retryDelayMs * 2, MAX_RETRY_MS);
  };

  const run = async () => {
    if (!active) return;
    const runController = new AbortController();
    controller = runController;

    // Watchdog: aborting this run's controller drops the pending read into the
    // catch below, which flips connected=false and reconnects — the same path
    // as a network-level drop.
    let stallTimer: ReturnType<typeof setTimeout> | null = null;
    const armStallTimer = () => {
      if (stallTimer) clearTimeout(stallTimer);
      stallTimer = setTimeout(() => runController.abort(), STALL_TIMEOUT_MS);
    };

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
        await refreshSpaToken();
        scheduleReconnect();
        return;
      }

      if (!response.ok || !response.body) {
        scheduleReconnect();
        return;
      }

      onConnectionChange?.(true);
      retryDelayMs = INITIAL_RETRY_MS;
      armStallTimer();

      const reader = response.body.getReader();
      const decoder = new TextDecoder();
      let buffer = '';

      for (;;) {
        const { done, value } = await reader.read();
        if (done) break;
        // Any chunk — heartbeat comments included — proves the connection is alive.
        armStallTimer();
        buffer += decoder.decode(value, { stream: true });

        // SSE frames are separated by a blank line.
        let separatorIndex = buffer.indexOf('\n\n');
        while (separatorIndex !== -1) {
          const frame = buffer.slice(0, separatorIndex);
          buffer = buffer.slice(separatorIndex + 2);
          dispatchFrame(frame, onEvent);
          separatorIndex = buffer.indexOf('\n\n');
        }
      }
    } catch {
      // Aborted by disconnect() or the stall watchdog, or the connection
      // dropped — fall through.
    } finally {
      if (stallTimer) clearTimeout(stallTimer);
    }

    if (active) {
      onConnectionChange?.(false);
      scheduleReconnect();
    }
  };

  void run();

  return () => {
    active = false;
    if (retryTimer) clearTimeout(retryTimer);
    controller?.abort();
    onConnectionChange?.(false);
  };
}

function dispatchFrame(frame: string, onEvent: (event: PlatformEventFrame) => void): void {
  // Comment frames (": connected", ": keepalive") have no data lines.
  const data = frame
    .split('\n')
    .filter(line => line.startsWith('data: '))
    .map(line => line.slice('data: '.length))
    .join('\n');

  if (!data) return;

  try {
    onEvent(JSON.parse(data) as PlatformEventFrame);
  } catch {
    // Malformed frame — ignore rather than kill the stream.
  }
}
