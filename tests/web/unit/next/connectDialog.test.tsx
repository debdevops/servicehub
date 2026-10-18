import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
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
import { expectNoAxeViolations } from '@tests/support/axe'
import { agent, component, coverageRows, me, page } from './homeData'

vi.mock('@/lib/api/coverage', async (o) => ({ ...(await o<typeof coverageApi>()), fetchCoverage: vi.fn() }))
vi.mock('@/lib/api/components', async (o) => ({ ...(await o<typeof componentsApi>()), fetchComponents: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn() }))
vi.mock('@/lib/api/pendingWork', async (o) => ({ ...(await o<typeof pendingApi>()), fetchPendingWork: vi.fn() }))
vi.mock('@/lib/api/namespaces', async (o) => ({
  ...(await o<typeof namespacesApi>()), fetchNamespaces: vi.fn(), connectNamespace: vi.fn(), testConnection: vi.fn(), removeNamespace: vi.fn(), fetchNamespaceStats: vi.fn(),
}))

const at = '2026-10-18T10:00:00Z'
function open(u: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter([nextRoute], { initialEntries: [u] })
  return { router, ...render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>) }
}

describe('Connect dialog (unit F1)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [component()], generatedAtUtc: at })
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([]))
    vi.mocked(namespacesApi.fetchNamespaces).mockResolvedValue([])
  })

  it('opens from a connection list on the cloud you are on, and creates nothing on open', async () => {
    const user = userEvent.setup()
    open('/next/aws/c/sqs')
    await user.click(await screen.findByRole('link', { name: /Connect a/ }))
    const dialog = await screen.findByRole('dialog')
    expect(await within(dialog).findByRole('button', { name: 'Connect' })).toBeInTheDocument()
    expect(namespacesApi.connectNamespace).not.toHaveBeenCalled()
  })

  it('refuses someone who is not an Admin, saying who can grant it, and creates nothing', async () => {
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me({ governanceActive: true, effectiveRole: 'Operator', recoverRole: 'Operator' }))
    open('/next/azure?dialog=connect')
    const dialog = await screen.findByRole('dialog')
    expect((await within(dialog).findAllByText(/you need the Admin role/)).length).toBeGreaterThan(0)
    expect(namespacesApi.connectNamespace).not.toHaveBeenCalled()
  })

  it('is no longer the “not built yet” frame, and has no accessibility violations', async () => {
    open('/next/azure?dialog=connect')
    const dialog = await screen.findByRole('dialog')
    expect(screen.queryByText(/Not built yet/)).not.toBeInTheDocument()
    await waitFor(() => expect(within(dialog).getAllByRole('textbox').length).toBeGreaterThan(0))
    await expectNoAxeViolations(dialog)
  })
})
