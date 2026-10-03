import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { MessageCell } from '@/components/message/MessageCell'
import { gistOfMessage } from '@/lib/api/messageGist'
import type { Message } from '@/lib/api/messages'

describe('the message cell', () => {
  it('shows the body start, its labels and the ID', () => {
    render(<MessageCell messageId="m-1" gist={{ preview: '{"orderId":"ORD-1"}', contentType: 'application/json', correlationId: 'corr-9', sessionId: null, properties: { source: 'checkout' } }} />)
    expect(screen.getByText('{"orderId":"ORD-1"}')).toBeInTheDocument()
    expect(screen.getByText('json')).toBeInTheDocument()
    expect(screen.getByText('corr-9')).toBeInTheDocument()
    expect(screen.getByText('checkout')).toBeInTheDocument()
    expect(screen.getByText('m-1')).toBeInTheDocument()
  })

  it('says so when nothing was recorded, and still shows the ID', () => {
    render(<MessageCell messageId="m-2" gist={null} />)
    expect(screen.getByText('No body recorded')).toBeInTheDocument()
    expect(screen.getByText('m-2')).toBeInTheDocument()
  })

  it('builds the same gist from a peeked message: one-line body, plain properties only, at most three', () => {
    const m = { messageId: 'x', body: '{\n  "a": 1\n}', contentType: 'text/plain', correlationId: null, sessionId: 's', applicationProperties: { a: '1', b: 2, c: { nested: true }, d: 'x', e: 'y' } } as unknown as Message
    const g = gistOfMessage(m)
    expect(g.preview).toBe('{ "a": 1 }')
    expect(g.properties).toEqual({ a: '1', b: '2', d: 'x' })
  })
})
