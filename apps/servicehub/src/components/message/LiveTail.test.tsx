import { act, render, screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import * as api from '../../lib/api/messages'
import { LiveTail, TAIL_EVERY_MS } from './LiveTail'

vi.mock('../../lib/api/messages')

const msg = (n: number) => ({ messageId: `m${n}`, sequenceNumber: n, body: `{"n":${n}}`, contentType: null, correlationId: null, sessionId: null, subject: null, enqueuedTime: '2026-09-26T10:00:00Z', deliveryCount: 1, deadLetterReason: null, deadLetterErrorDescription: null, applicationProperties: {}, sizeInBytes: 10, isFromDeadLetter: false })
const page = (...ns: number[]) => ({ namespaceId: 'n', entity: 'orders', subscription: null, deadLetter: false, messages: ns.map(msg), paging: { max: 50, returned: ns.length, nextFromSequenceNumber: null }, peek: { repeatable: true, warning: null } }) as unknown as api.PeekPage

describe('Follow live', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('looks from the last message it saw, puts arrivals on top, and stops looking while paused', async () => {
    const peek = vi.mocked(api.peekMessages)
    peek.mockResolvedValueOnce(page(1, 2)).mockResolvedValueOnce(page(3)).mockResolvedValue(page())
    render(<LiveTail namespaceId="n" entity="orders" />)
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    expect(peek).toHaveBeenLastCalledWith('n', expect.objectContaining({ from: undefined }))

    await act(async () => { await vi.advanceTimersByTimeAsync(TAIL_EVERY_MS) })
    expect(peek).toHaveBeenLastCalledWith('n', expect.objectContaining({ from: 3 }))
    const items = within(screen.getByRole('list', { name: 'Arrived messages' })).getAllByRole('listitem')
    expect(items.map((li) => li.textContent)).toEqual([expect.stringContaining('#3'), expect.stringContaining('#2'), expect.stringContaining('#1')])

    act(() => screen.getByRole('button', { name: /Pause/ }).click())
    const calls = peek.mock.calls.length
    await act(async () => { await vi.advanceTimersByTimeAsync(TAIL_EVERY_MS * 3) })
    expect(peek.mock.calls.length).toBe(calls)
  })

  it('stops looking when it closes', async () => {
    const peek = vi.mocked(api.peekMessages)
    peek.mockResolvedValue(page())
    const view = render(<LiveTail namespaceId="n" entity="orders" />)
    await act(async () => { await vi.advanceTimersByTimeAsync(0) })
    view.unmount()
    const calls = peek.mock.calls.length
    await act(async () => { await vi.advanceTimersByTimeAsync(TAIL_EVERY_MS * 3) })
    expect(peek.mock.calls.length).toBe(calls)
  })
})
