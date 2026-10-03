import { beforeEach, describe, expect, it, vi } from 'vitest'
import { api, sessionId } from '@/lib/api/client'
import { fetchAudit, fetchMe } from '@/lib/api/identity'

vi.mock('@/lib/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/api/client')>()
  return { ...actual, api: { get: vi.fn() } }
})

const mocked = vi.mocked(api)

describe('identity api', () => {
  beforeEach(() => vi.clearAllMocks())

  it('reads who is acting from /me', async () => {
    mocked.get.mockResolvedValue({ data: { authMethod: 'session' } })

    await fetchMe()

    expect(mocked.get).toHaveBeenCalledWith('/me')
  })

  it('passes audit filters as query parameters', async () => {
    mocked.get.mockResolvedValue({ data: { items: [], page: 1, pageSize: 50, total: 0 } })

    await fetchAudit({ pageSize: 10, namespaceId: 'n1' })

    expect(mocked.get).toHaveBeenCalledWith('/audit', { params: { pageSize: 10, namespaceId: 'n1' } })
  })
})

describe('sessionId', () => {
  beforeEach(() => window.sessionStorage.clear())

  it('is stable for the session and looks like an id the API will accept', () => {
    const first = sessionId()

    expect(sessionId()).toBe(first)
    expect(first).toMatch(/^[A-Za-z0-9-]{8,64}$/)
  })

  it('still answers when storage is blocked', () => {
    const blocked = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked')
    })

    expect(sessionId()).toMatch(/^[A-Za-z0-9-]{8,64}$/)
    blocked.mockRestore()
  })
})
