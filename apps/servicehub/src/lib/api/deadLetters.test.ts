import { beforeEach, describe, expect, it, vi } from 'vitest'
import { api } from './client'
import { fetchDeadLetters } from './deadLetters'

vi.mock('./client', () => ({ api: { get: vi.fn() } }))
const mocked = vi.mocked(api)

describe('dead letters api', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocked.get.mockResolvedValue({ data: { items: [] } })
  })

  it('asks for one cloud, in the API’s spelling, and leaves out what is off', async () => {
    await fetchDeadLetters({ provider: 'aws', range: 'all', noReason: false, page: 2, q: 'orders' })

    expect(mocked.get).toHaveBeenCalledWith('/dead-letters', {
      params: { provider: 'Aws', range: undefined, noReason: undefined, page: 2, q: 'orders' },
    })
  })

  it('passes a window and a reason through', async () => {
    await fetchDeadLetters({ provider: 'azure', range: '7d', reason: 'TimedOut' })

    expect(mocked.get).toHaveBeenCalledWith('/dead-letters', {
      params: expect.objectContaining({ provider: 'Azure', range: '7d', reason: 'TimedOut' }),
    })
  })
})
