import { beforeEach, describe, expect, it, vi } from 'vitest'
import { api } from './client'
import {
  connectNamespace,
  fetchEntities,
  removeNamespace,
  testConnection,
  type NamespaceStats,
} from './namespaces'

vi.mock('./client', () => ({ api: { get: vi.fn(), post: vi.fn(), delete: vi.fn() } }))

const mocked = vi.mocked(api)

describe('namespaces api', () => {
  beforeEach(() => vi.clearAllMocks())

  it('declares its intent when connecting, and only then', async () => {
    mocked.post.mockResolvedValue({ data: { id: 'n1' } })

    await connectNamespace({ name: 'acme-bus', provider: 'azure', authType: 'connectionString', connectionString: 'x' })

    expect(mocked.post).toHaveBeenCalledWith(
      '/namespaces',
      expect.objectContaining({ name: 'acme-bus' }),
      { headers: { 'X-ServiceHub-Intent': 'create-namespace' } },
    )
  })

  it('declares its intent when removing', async () => {
    mocked.delete.mockResolvedValue({})

    await removeNamespace('n1')

    expect(mocked.delete).toHaveBeenCalledWith('/namespaces/n1', {
      headers: { 'X-ServiceHub-Intent': 'delete-namespace' },
    })
  })

  it('tests a connection without an intent header — a probe changes nothing', async () => {
    mocked.post.mockResolvedValue({ data: { isConnected: true, message: 'ok', testedAt: 'now' } })

    await testConnection('n1')

    expect(mocked.post).toHaveBeenCalledWith('/namespaces/n1/test-connection')
  })

  it('asks one endpoint for every kind, filtering by query parameter', async () => {
    mocked.get.mockResolvedValue({ data: { namespaceId: 'n1', entities: [] } })

    await fetchEntities('n1')
    await fetchEntities('n1', 'queue')

    expect(mocked.get).toHaveBeenNthCalledWith(1, '/namespaces/n1/entities', { params: undefined })
    expect(mocked.get).toHaveBeenNthCalledWith(2, '/namespaces/n1/entities', { params: { kind: 'queue' } })
  })

  it('keeps "cannot count" distinct from zero in the stats type', () => {
    const cannotCount: NamespaceStats = {
      namespaceId: 'n1',
      entities: [],
      activeMessages: null,
      deadLetterMessages: null,
      messageCountsSupported: false,
      observedAt: 'now',
    }

    expect(cannotCount.activeMessages).toBeNull()
    expect(cannotCount.activeMessages).not.toBe(0)
  })
})
