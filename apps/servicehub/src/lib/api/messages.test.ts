import { beforeEach, describe, expect, it, vi } from 'vitest'
import { api } from './client'
import { canAutoRefresh, fetchMessage, peekDeadLetters, peekMessages, type PeekPage } from './messages'

vi.mock('./client', () => ({ api: { get: vi.fn() } }))
const mocked = vi.mocked(api)

const page = (repeatable: boolean): PeekPage => ({
  namespaceId: 'n1', entity: 'orders', subscription: null, deadLetter: true, messages: [],
  paging: { requested: 25, returned: 0, nextFromSequenceNumber: null },
  peek: { repeatable, warning: repeatable ? null : 'counts as a delivery attempt' },
})

describe('messages api', () => {
  beforeEach(() => vi.clearAllMocks())

  it('peeks each side from its own endpoint and passes the paging cursor', async () => {
    mocked.get.mockResolvedValue({ data: page(true) })

    await peekMessages('n1', { entity: 'orders', max: 10, from: 41 })
    await peekDeadLetters('n1', { entity: 'events', subscription: 'billing' })

    expect(mocked.get).toHaveBeenNthCalledWith(1, '/namespaces/n1/messages/peek', {
      params: { entity: 'orders', subscription: undefined, max: 10, from: 41 },
    })
    expect(mocked.get).toHaveBeenNthCalledWith(2, '/namespaces/n1/dead-letter/peek', {
      params: { entity: 'events', subscription: 'billing', max: undefined, from: undefined },
    })
  })

  it('looks up one message by sequence number', async () => {
    mocked.get.mockResolvedValue({ data: { sequenceNumber: 7 } })

    await fetchMessage('n1', 7, { entity: 'orders', deadLetter: true })

    expect(mocked.get).toHaveBeenCalledWith('/namespaces/n1/messages/7', {
      params: { entity: 'orders', subscription: undefined, deadLetter: true },
    })
  })

  it('refreshes automatically only where the API says peeking is repeatable — never from a provider name', () => {
    expect(canAutoRefresh(page(true))).toBe(true)
    expect(canAutoRefresh(page(false))).toBe(false)
    expect(canAutoRefresh(undefined)).toBe(false)
  })
})
