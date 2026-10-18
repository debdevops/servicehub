import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as agentsApi from '@/lib/api/agents'
import * as componentsApi from '@/lib/api/components'
import * as coverageApi from '@/lib/api/coverage'
import * as identityApi from '@/lib/api/identity'
import * as pendingApi from '@/lib/api/pendingWork'
import * as settingsApi from '@/lib/api/settings'
import { nextRoute } from '@/next/router'
import { expectNoAxeViolations } from '@tests/support/axe'
import { agent, approval, component, coverageRows, handOff, me, page } from './homeData'

vi.mock('@/lib/api/coverage', async (o) => ({ ...(await o<typeof coverageApi>()), fetchCoverage: vi.fn() }))
vi.mock('@/lib/api/components', async (o) => ({ ...(await o<typeof componentsApi>()), fetchComponents: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn() }))
vi.mock('@/lib/api/pendingWork', async (o) => ({ ...(await o<typeof pendingApi>()), fetchPendingWork: vi.fn() }))

function open(url: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter([nextRoute], { initialEntries: [url] })
  return { router, ...render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>) }
}
const hoursAgo = (h: number) => new Date(Date.now() - h * 3_600_000).toISOString()

describe('Needs your attention (unit E1)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [component()], generatedAtUtc: new Date().toISOString() })
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([
      approval({ id: 'a1', entryId: 'a1', dlqMessageId: 41, since: hoursAgo(3) }),
      approval({ id: 'a2', entryId: 'a2', dlqMessageId: 43, componentKey: 'storage-queues', since: hoursAgo(30) }),
      handOff({ id: 'h1', entryId: 'h1', dlqMessageId: 42, since: hoursAgo(2) }),
      { ...approval({ id: 'r1', entryId: null }), kind: 'rule', type: null, componentKey: null },
    ]))
  })

  it('asks for its own cloud, and for every cloud when the scope is all', async () => {
    open('/next/azure/attention')
    await screen.findByRole('heading', { level: 1, name: /Needs your attention/ })
    await waitFor(() => expect(pendingApi.fetchPendingWork).toHaveBeenCalledWith({ provider: 'azure' }))
    open('/next/all/attention')
    await waitFor(() => expect(pendingApi.fetchPendingWork).toHaveBeenCalledWith({}))
  })

  it('counts only what needs a person (a rule item is not one), split into approvals and hand-offs, with the oldest', async () => {
    open('/next/azure/attention')
    const approvals = (await screen.findByText('The Agent stopped and asked')).closest('div')!.parentElement!
    expect(within(approvals).getByText('2')).toBeInTheDocument()
    expect(screen.getByText('ServiceHub stopped; a person decides').closest('div')!.parentElement!).toHaveTextContent('1')
    expect(screen.getByText(/#43 on orders-queue/)).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'All (3)' })).toBeInTheDocument()
  })

  it('keeps the tab and the filters in the address, and narrows the tiles with them', async () => {
    const user = userEvent.setup()
    const { router } = open('/next/azure/attention')
    await user.click(await screen.findByRole('tab', { name: /Hand-offs \(1\)/ }))
    expect(router.state.location.search).toContain('type=handOffs')
    await user.click(screen.getByRole('tab', { name: /^All/ }))
    expect(router.state.location.search).not.toContain('type=')
    await user.selectOptions(screen.getByLabelText('Age:'), 'over 1 day')
    expect(router.state.location.search).toContain('age=24h')
    expect(screen.getByRole('tab', { name: 'All (1)' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Clear filters' }))
    expect(screen.getByRole('tab', { name: 'All (3)' })).toBeInTheDocument()
  })

  it('offers only components and families that have something waiting', async () => {
    open('/next/azure/attention')
    const component = await screen.findByLabelText('Component:')
    expect(within(component).getAllByRole('option').map((o) => o.textContent)).toEqual(['all', 'Service Bus', 'Storage Queues'])
    expect(within(screen.getByLabelText('Family:')).getAllByRole('option')).toHaveLength(2)
  })

  it('says so when nothing waits, and when the read fails shows no list at all', async () => {
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([]))
    open('/next/azure/attention')
    expect(await screen.findByText('Nothing here needs a person right now.')).toBeInTheDocument()
    expect(screen.getByText('Nothing is waiting')).toBeInTheDocument()
  })

  it('shows an error, not an empty list, when the read fails', async () => {
    vi.mocked(pendingApi.fetchPendingWork).mockRejectedValue(new Error('boom'))
    open('/next/azure/attention')
    expect(await screen.findByRole('alert')).toHaveTextContent('could not be read')
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })

  it('adds a Cloud column only for all clouds', async () => {
    open('/next/all/attention')
    expect(await screen.findByRole('columnheader', { name: 'Cloud' })).toBeInTheDocument()
  })

  it('has no accessibility violations', async () => {
    const { container } = open('/next/azure/attention')
    await screen.findByRole('table')
    await expectNoAxeViolations(container)
  })
})
