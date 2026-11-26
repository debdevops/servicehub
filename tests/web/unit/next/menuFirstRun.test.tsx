import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as agentsApi from '@/lib/api/agents'
import * as componentsApi from '@/lib/api/components'
import * as coverageApi from '@/lib/api/coverage'
import * as identityApi from '@/lib/api/identity'
import * as namespacesApi from '@/lib/api/namespaces'
import * as pendingApi from '@/lib/api/pendingWork'
import * as settingsApi from '@/lib/api/settings'
import { nextRoute } from '@/next/router'
import { agent, coverageRows, me, page } from './homeData'

vi.mock('@/lib/api/coverage', async (o) => ({ ...(await o<typeof coverageApi>()), fetchCoverage: vi.fn() }))
vi.mock('@/lib/api/components', async (o) => ({ ...(await o<typeof componentsApi>()), fetchComponents: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn() }))
vi.mock('@/lib/api/pendingWork', async (o) => ({ ...(await o<typeof pendingApi>()), fetchPendingWork: vi.fn() }))
vi.mock('@/lib/api/namespaces', async (o) => ({ ...(await o<typeof namespacesApi>()), fetchNamespaces: vi.fn() }))

const at = '2026-10-18T10:00:00Z'
const failing = { id: 'n1', name: 'orders-sb', displayName: null, provider: 'azure', environment: 'dev', authType: 'connectionString', isActive: true, createdAt: at, lastConnectionTestSucceeded: false } as unknown as namespacesApi.Namespace

async function open() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(<QueryClientProvider client={client}><RouterProvider router={createMemoryRouter([nextRoute], { initialEntries: ['/'] })} /></QueryClientProvider>)
  await screen.findByRole('banner')
  return screen.getByRole('navigation', { name: 'ServiceHub' })
}

describe('the menu on a first run (nothing connected)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [], generatedAtUtc: at })
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([]))
  })

  it('with no connection at all, shows only Home, Connections and the reference pages; the components and tools are not drawn', async () => {
    vi.mocked(namespacesApi.fetchNamespaces).mockResolvedValue([])
    const menu = await open()
    await within(menu).findByRole('link', { name: /^Connections/ })
    await vi.waitFor(() => expect(within(menu).queryByRole('heading', { name: /PaaS Components/ })).not.toBeInTheDocument())
    expect(within(menu).queryByRole('heading', { name: /Operational Tools/ })).not.toBeInTheDocument()
    expect(within(menu).queryByRole('link', { name: 'Needs your attention' })).not.toBeInTheDocument()
    expect(within(menu).getByRole('link', { name: 'Home' })).toBeInTheDocument()
    expect(within(menu).getByRole('link', { name: 'What ServiceHub covers' })).toBeInTheDocument()
    expect(within(menu).getByRole('link', { name: 'Configuration' })).toBeInTheDocument()
    // The safety control is not hidden by being first-run.
    expect(screen.getByRole('link', { name: /Emergency stop/ })).toBeInTheDocument()
  })

  it('one connection, even a failing one, brings the components and tools back', async () => {
    vi.mocked(namespacesApi.fetchNamespaces).mockResolvedValue([failing])
    const menu = await open()
    expect(await within(menu).findByRole('heading', { name: /Operational Tools/ })).toBeInTheDocument()
    expect(within(menu).getByRole('link', { name: 'Needs your attention' })).toBeInTheDocument()
  })

  it('a connections read that fails hides nothing: the full menu stays', async () => {
    vi.mocked(namespacesApi.fetchNamespaces).mockRejectedValue(new Error('down'))
    const menu = await open()
    expect(await within(menu).findByRole('heading', { name: /Operational Tools/ })).toBeInTheDocument()
  })
})
