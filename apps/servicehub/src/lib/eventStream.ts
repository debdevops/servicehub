import { isDemo } from './demo/state'
/**
 * The live event stream, one connection for the whole app.
 *
 * A stream is a HINT, never a feed (unit 2.11): the server drops the oldest events under pressure and does not
 * replay to a client that reconnects. So an event means only "something changed — look again", and every screen
 * still reads the durable tables. What this module owns is the connection and whether it is open, so a screen can
 * say "Live" only while that is true and say nothing false when it is not.
 */
export type StreamStatus = 'live' | 'connecting' | 'offline'

export interface StreamEvent {
  readonly id: string
  readonly eventType: string
  readonly category: string
  readonly occurredUtc: string
  readonly cloudProvider: string | null
  readonly namespaceId: string | null
}

type Listener = (event: StreamEvent) => void

const URL = '/api/v1/events/stream'

let source: EventSource | null = null
let status: StreamStatus = 'offline'
const listeners = new Set<Listener>()
const statusListeners = new Set<() => void>()

function setStatus(next: StreamStatus) {
  if (status === next) return
  status = next
  statusListeners.forEach((l) => l())
}

/** Opens the stream if it is not open. Safe to call again. Without EventSource (a test, an old browser) it stays offline. */
export function startEventStream(): void {
  // A demo has no server to stream from; its data does not change underneath anyone.
  if (source || typeof EventSource === 'undefined' || isDemo()) return
  setStatus('connecting')
  source = new EventSource(URL)
  source.onopen = () => setStatus('live')
  // The browser reconnects by itself; until it does, "Live" would be a lie.
  source.onerror = () => setStatus(source?.readyState === EventSource.CLOSED ? 'offline' : 'connecting')
  source.onmessage = (message: MessageEvent<string>) => {
    try {
      const event = JSON.parse(message.data) as StreamEvent
      listeners.forEach((l) => l(event))
    } catch {
      /* a frame that is not ours is ignored — the stream is a hint */
    }
  }
}

export function stopEventStream(): void {
  source?.close()
  source = null
  setStatus('offline')
}

export const onStreamEvent = (listener: Listener) => {
  listeners.add(listener)
  return () => void listeners.delete(listener)
}

export const getStreamStatus = () => status
export const subscribeStreamStatus = (listener: () => void) => {
  statusListeners.add(listener)
  return () => void statusListeners.delete(listener)
}
