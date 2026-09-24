import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { getStreamStatus, onStreamEvent, startEventStream, stopEventStream } from './eventStream'

class FakeSource {
  static last: FakeSource
  static CLOSED = 2
  readyState = 0
  onopen: (() => void) | null = null
  onerror: (() => void) | null = null
  onmessage: ((m: MessageEvent<string>) => void) | null = null
  closed = false
  constructor(public url: string) { FakeSource.last = this }
  close() { this.closed = true; this.readyState = 2 }
  say(data: string) { this.onmessage?.({ data } as MessageEvent<string>) }
}

describe('the live event stream', () => {
  beforeEach(() => vi.stubGlobal('EventSource', FakeSource))
  afterEach(() => { stopEventStream(); vi.unstubAllGlobals() })

  it('says live only while the connection is open', () => {
    startEventStream()
    expect(getStreamStatus()).toBe('connecting')
    FakeSource.last.onopen?.()
    expect(getStreamStatus()).toBe('live')
    FakeSource.last.readyState = 0
    FakeSource.last.onerror?.()
    expect(getStreamStatus()).toBe('connecting') // the browser is retrying; "Live" would be false
    FakeSource.last.readyState = 2
    FakeSource.last.onerror?.()
    expect(getStreamStatus()).toBe('offline')
  })

  it('hands events to listeners and ignores a frame that is not one', () => {
    const seen = vi.fn()
    const off = onStreamEvent(seen)
    startEventStream()
    FakeSource.last.say('not json')
    FakeSource.last.say(JSON.stringify({ id: '1', eventType: 'x', category: 'dlq', occurredUtc: 'now', cloudProvider: null, namespaceId: null }))
    expect(seen).toHaveBeenCalledTimes(1)
    off()
  })

  it('opens one connection however many times it is asked', () => {
    startEventStream()
    const first = FakeSource.last
    startEventStream()
    expect(FakeSource.last).toBe(first)
  })

  it('stays offline where there is no EventSource', () => {
    vi.unstubAllGlobals()
    vi.stubGlobal('EventSource', undefined)
    startEventStream()
    expect(getStreamStatus()).toBe('offline')
  })
})
