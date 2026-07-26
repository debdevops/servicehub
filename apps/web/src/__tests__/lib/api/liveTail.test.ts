import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { connectLiveTail } from '@/lib/api/liveTail';
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

const messageFrame =
  'data: {"messageId":"m1","sequenceNumber":1,"body":"{}","enqueuedTime":"2026-07-08T00:00:00Z","deliveryCount":1,"state":"Active","contentType":null}\n\n';

describe('connectLiveTail', () => {
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

  it('parses data frames into messages and ignores comment frames', async () => {
    fetchMock.mockResolvedValue(sseResponse([': connected\n\n', messageFrame, ': heartbeat\n\n']));
    const onMessage = vi.fn();

    disconnect = connectLiveTail({ namespaceId: 'ns-1', entityName: 'orders', onMessage });

    await vi.waitFor(() => expect(onMessage).toHaveBeenCalledTimes(1));
    expect(onMessage).toHaveBeenCalledWith(expect.objectContaining({ messageId: 'm1' }));
  });

  it('builds the query string from namespaceId, entityName, subscriptionName, fromDeadLetter', async () => {
    fetchMock.mockResolvedValue(sseResponse([': connected\n\n']));

    disconnect = connectLiveTail({
      namespaceId: 'ns-1',
      entityName: 'orders-topic',
      subscriptionName: 'orders-sub',
      fromDeadLetter: true,
      onMessage: vi.fn(),
    });

    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalled());
    const [url] = fetchMock.mock.calls[0];
    expect(url).toContain('/messages/live-tail?');
    expect(url).toContain('namespaceId=ns-1');
    expect(url).toContain('entityName=orders-topic');
    expect(url).toContain('subscriptionName=orders-sub');
    expect(url).toContain('fromDeadLetter=true');
  });

  it('reports connection state up and down around the stream lifetime', async () => {
    fetchMock.mockResolvedValue(sseResponse([': connected\n\n']));
    const onConnectionChange = vi.fn();

    disconnect = connectLiveTail({
      namespaceId: 'ns-1',
      entityName: 'orders',
      onMessage: vi.fn(),
      onConnectionChange,
    });

    await vi.waitFor(() => expect(onConnectionChange).toHaveBeenCalledWith(true));
    await vi.waitFor(() => expect(onConnectionChange).toHaveBeenCalledWith(false));
  });

  it('invokes onUnsupported on HTTP 409 without treating it as a normal disconnect', async () => {
    fetchMock.mockResolvedValue({ ok: false, status: 409, body: null } as unknown as Response);
    const onUnsupported = vi.fn();
    const onConnectionChange = vi.fn();

    disconnect = connectLiveTail({
      namespaceId: 'ns-1',
      entityName: 'checkout-queue',
      onMessage: vi.fn(),
      onUnsupported,
      onConnectionChange,
    });

    await vi.waitFor(() => expect(onUnsupported).toHaveBeenCalledTimes(1));
    expect(onConnectionChange).not.toHaveBeenCalledWith(true);
  });

  it('invokes onSessionExpired for the session-expired comment frame', async () => {
    fetchMock.mockResolvedValue(sseResponse([': connected\n\n', ': session-expired\n\n']));
    const onSessionExpired = vi.fn();

    disconnect = connectLiveTail({
      namespaceId: 'ns-1',
      entityName: 'orders',
      onMessage: vi.fn(),
      onSessionExpired,
    });

    await vi.waitFor(() => expect(onSessionExpired).toHaveBeenCalledTimes(1));
  });

  it('does not auto-reconnect after the stream ends', async () => {
    fetchMock.mockResolvedValue(sseResponse([': connected\n\n']));

    disconnect = connectLiveTail({ namespaceId: 'ns-1', entityName: 'orders', onMessage: vi.fn() });

    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    // Give any (incorrect) reconnect timer a chance to fire.
    await new Promise(resolve => setTimeout(resolve, 50));
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('refreshes the SPA token on 401', async () => {
    fetchMock.mockResolvedValue({ ok: false, status: 401, body: null } as unknown as Response);

    disconnect = connectLiveTail({ namespaceId: 'ns-1', entityName: 'orders', onMessage: vi.fn() });

    await vi.waitFor(() => expect(refreshSpaToken).toHaveBeenCalled());
  });

  it('disconnect aborts the in-flight request', async () => {
    let capturedSignal: AbortSignal | undefined;
    fetchMock.mockImplementation((_url: string, init: RequestInit) => {
      capturedSignal = init.signal ?? undefined;
      return new Promise(() => {}); // never resolves
    });

    disconnect = connectLiveTail({ namespaceId: 'ns-1', entityName: 'orders', onMessage: vi.fn() });
    await vi.waitFor(() => expect(capturedSignal).toBeDefined());

    disconnect();
    disconnect = null;

    expect(capturedSignal!.aborted).toBe(true);
  });

  it('ignores malformed JSON frames without killing the stream', async () => {
    fetchMock.mockResolvedValue(sseResponse(['data: {not-json\n\n', messageFrame]));
    const onMessage = vi.fn();

    disconnect = connectLiveTail({ namespaceId: 'ns-1', entityName: 'orders', onMessage });

    await vi.waitFor(() => expect(onMessage).toHaveBeenCalledTimes(1));
    expect(onMessage.mock.calls[0][0].messageId).toBe('m1');
  });
});
