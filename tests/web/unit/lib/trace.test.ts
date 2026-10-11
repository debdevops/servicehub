import { describe, expect, it } from 'vitest'
import { findTrace, isTraceTemplate, traceLink } from '@/lib/trace'

const json = (o: unknown) => JSON.stringify(o)

describe('open the trace', () => {
  it('reads a W3C trace id from traceparent, and from Service Bus’s Diagnostic-Id', () => {
    expect(findTrace(json({ traceparent: '00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01' }))).toEqual({ traceId: '4bf92f3577b34da6a3ce929d0e0e4736', from: 'traceparent' })
    expect(findTrace(json({ 'Diagnostic-Id': '00-4BF92F3577B34DA6A3CE929D0E0E4736-00f067aa0ba902b7-00' }))?.traceId).toBe('4bf92f3577b34da6a3ce929d0e0e4736')
  })

  it('reads an X-Ray root from AWS’s trace header', () => {
    expect(findTrace(json({ AWSTraceHeader: 'Root=1-5759e988-bd862e3fe1be46a994272793;Parent=53995c3f42cd8ad8;Sampled=1' }))).toEqual({ traceId: '1-5759e988-bd862e3fe1be46a994272793', from: 'AWSTraceHeader' })
  })

  it('reads a bare trace id property', () => {
    expect(findTrace(json({ traceId: '4bf92f3577b34da6a3ce929d0e0e4736' }))?.from).toBe('traceId')
  })

  it('finds nothing where there is nothing — it never makes one up', () => {
    expect(findTrace(null)).toBeNull()
    expect(findTrace('not json')).toBeNull()
    expect(findTrace(json(['a']))).toBeNull()
    expect(findTrace(json({ source: 'checkout' }))).toBeNull()
    expect(findTrace(json({ traceparent: 'garbage' }))).toBeNull()
    expect(findTrace(json({ traceparent: `00-${'0'.repeat(32)}-00f067aa0ba902b7-01` }))).toBeNull()
    expect(findTrace(json({ traceparent: 42 }))).toBeNull()
  })

  it('builds a link only from a usable address', () => {
    expect(traceLink('https://tracing.example.com/trace/{traceId}', 'abc')).toBe('https://tracing.example.com/trace/abc')
    expect(traceLink('https://x.example/?q={traceId}', '1-5759e988-bd86')).toBe('https://x.example/?q=1-5759e988-bd86')
    expect(traceLink('', 'abc')).toBeNull()
    expect(traceLink('https://tracing.example.com/trace/', 'abc')).toBeNull()
    // Only web addresses: a pasted script or file link is never turned into something clickable.
    expect(isTraceTemplate('javascript:alert({traceId})')).toBe(false)
    expect(isTraceTemplate('file:///etc/{traceId}')).toBe(false)
    expect(isTraceTemplate('not a url {traceId}')).toBe(false)
  })
})
