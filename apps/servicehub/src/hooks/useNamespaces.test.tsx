import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { renderHook, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as namespacesApi from '../lib/api/namespaces'
import { namespaceKeys, useConnectNamespace, useNamespaces } from './useNamespaces'

vi.mock('../lib/api/namespaces')

const mocked = vi.mocked(namespacesApi)

function wrapper() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  const invalidate = vi.spyOn(client, 'invalidateQueries')
  const Wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  )
  return { Wrapper, invalidate }
}

describe('useNamespaces', () => {
  beforeEach(() => vi.clearAllMocks())

  it('lists what the API returns', async () => {
    mocked.fetchNamespaces.mockResolvedValue([])
    const { Wrapper } = wrapper()

    const { result } = renderHook(() => useNamespaces(), { wrapper: Wrapper })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(result.current.data).toEqual([])
  })

  it('refreshes every namespace query after connecting one', async () => {
    mocked.connectNamespace.mockResolvedValue({ id: 'n1' } as namespacesApi.Namespace)
    const { Wrapper, invalidate } = wrapper()

    const { result } = renderHook(() => useConnectNamespace(), { wrapper: Wrapper })
    result.current.mutate({ name: 'acme-bus', provider: 'azure', authType: 'connectionString' })

    await waitFor(() => expect(invalidate).toHaveBeenCalledWith({ queryKey: namespaceKeys.all }))
  })
})
