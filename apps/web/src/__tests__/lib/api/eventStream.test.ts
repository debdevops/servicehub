import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { connectEventStream } from '@/lib/api/eventStream';
import { getSpaToken, refreshSpaToken } from '@/lib/api/client';

vi.mock('@/lib/api/client', () => ({
  apiClient: { defaults: { baseURL: '/api/v1' } },
  getSpaToken: vi.fn(() => 'test-token'),
  refreshSpaToken: vi.fn(async () => true),
}));

function sseResponse(chunks: string[], status = 200): Response {
  const encoder = new TextEncoder();
  const stream = new ReadableStream<Uint8Array>({
    start(controller) {
      for (const chunk of chunks) controller.enqueue(encoder.encode(chunk));
      controller.close();
    },
  });
  return { ok: status < 300, status, body: stream } as unknown as Response;
}

const spikeFrame =
  'data: {"id":"e1","eventType":"servicehub.dlq.spike.detected.v1","category":"dlq","severity":"warning","occurredUtc":"2026-07-08T00:00:00Z","namespaceId":"ns-1"}\n\n';

describe('connectEventStream', () => {
  const fetchMock = vi.fn();
  let disconnect: (() => void) | null = null;

  beforeEach(() => {
    vi.stubGlobal('fetch', fetchMock);
    fetchMock.mockReset();
    vi.mocked(getSpaToken).mockReturnValue('test-token');
    vi.mocked(refreshSpaToken).mockClear();
  });

  afterEach(() => {
    disconnect?.();
    disconnect = null;
    vi.unstubAllGlobals();
  });

  it('parses data frames into events and ignores comment frames', async () => {
    fetchMock.mockResolvedValue(sseResponse([': connected\n\n', spikeFrame, ': keepalive\n\n']));
    const onEvent = vi.fn();

    disconnect = connectEventStream({ onEvent });

    await vi.waitFor(() => expect(onEvent).toHaveBeenCalledTimes(1));
    expect(onEvent).toHaveBeenCalledWith(
      expect.objectContaining({
        eventType: 'servicehub.dlq.spike.detected.v1',
        namespaceId: 'ns-1',
        severity: 'warning',
      }),
    );
  });

  it('reassembles a frame split across chunks', async () => {
    const [head, tail] = [spikeFrame.slice(0, 40), spikeFrame.slice(40)];
    fetchMock.mockResolvedValue(sseResponse([head, tail]));
    const onEvent = vi.fn();

    disconnect = connectEventStream({ onEvent });

    await vi.waitFor(() => expect(onEvent).toHaveBeenCalledTimes(1));
  });

  it('ignores malformed JSON frames without killing the stream', async () => {
    fetchMock.mockResolvedValue(sseResponse(['data: {not-json\n\n', spikeFrame]));
    const onEvent = vi.fn();

    disconnect = connectEventStream({ onEvent });

    await vi.waitFor(() => expect(onEvent).toHaveBeenCalledTimes(1));
    expect(onEvent.mock.calls[0][0].id).toBe('e1');
  });

  it('sends the SPA token header and hits the events stream endpoint', async () => {
    fetchMock.mockResolvedValue(sseResponse([': connected\n\n']));

    disconnect = connectEventStream({ onEvent: vi.fn() });

    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalled());
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('/api/v1/events/stream');
    expect(init.headers['X-SPA-Token']).toBe('test-token');
    expect(init.headers.Accept).toBe('text/event-stream');
  });

  it('reports connection state up and down around the stream lifetime', async () => {
    fetchMock.mockResolvedValue(sseResponse([': connected\n\n']));
    const onConnectionChange = vi.fn();

    disconnect = connectEventStream({ onEvent: vi.fn(), onConnectionChange });

    await vi.waitFor(() => expect(onConnectionChange).toHaveBeenCalledWith(true));
    // The mocked stream closes immediately, so a drop must follow.
    await vi.waitFor(() => expect(onConnectionChange).toHaveBeenCalledWith(false));
  });

  it('refreshes the SPA token on 401 before reconnecting', async () => {
    fetchMock.mockResolvedValue({ ok: false, status: 401, body: null } as unknown as Response);

    disconnect = connectEventStream({ onEvent: vi.fn() });

    await vi.waitFor(() => expect(refreshSpaToken).toHaveBeenCalled());
  });

  it('disconnect aborts the in-flight request', async () => {
    let capturedSignal: AbortSignal | undefined;
    fetchMock.mockImplementation((_url: string, init: RequestInit) => {
      capturedSignal = init.signal ?? undefined;
      return new Promise(() => {}); // never resolves — stream stays "connecting"
    });

    disconnect = connectEventStream({ onEvent: vi.fn() });
    await vi.waitFor(() => expect(capturedSignal).toBeDefined());

    disconnect();
    disconnect = null;

    expect(capturedSignal!.aborted).toBe(true);
  });
});
