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
import * as replayApi from '@/lib/api/replay'
import * as settingsApi from '@/lib/api/settings'
import * as signaturesApi from '@/lib/api/signatures'
import { nextRoute } from '@/next/router'
import { expectNoAxeViolations } from '@tests/support/axe'
import { agent, approval, component, coverageRows, handOff, me, page } from './homeData'

vi.mock('@/lib/api/coverage', async (o) => ({ ...(await o<typeof coverageApi>()), fetchCoverage: vi.fn() }))
vi.mock('@/lib/api/components', async (o) => ({ ...(await o<typeof componentsApi>()), fetchComponents: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn() }))
vi.mock('@/lib/api/namespaces', async (o) => ({ ...(await o<typeof namespacesApi>()), fetchNamespaces: vi.fn() }))
vi.mock('@/lib/api/replay', async (o) => ({ ...(await o<typeof replayApi>()), fetchReplayProposal: vi.fn() }))
vi.mock('@/lib/api/signatures', async (o) => ({ ...(await o<typeof signaturesApi>()), fetchSignatures: vi.fn() }))
vi.mock('@/lib/api/pendingWork', async (o) => ({ ...(await o<typeof pendingApi>()), fetchPendingWork: vi.fn(), approvePending: vi.fn() }))

function open(url: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter([nextRoute], { initialEntries: [url] })
  return { router, ...render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>) }
}
const hoursAgo = (h: number) => new Date(Date.now() - h * 3_600_000).toISOString()

describe('Pending approvals (unit E2)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [component()], generatedAtUtc: new Date().toISOString() })
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
    vi.mocked(namespacesApi.fetchNamespaces).mockResolvedValue([])
    vi.mocked(signaturesApi.fetchSignatures).mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 4, all: 0, growing: 0, replayHelps: 0, replayDoesNotHelp: 0 })
    vi.mocked(replayApi.fetchReplayProposal).mockRejectedValue(new Error('not needed'))
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([
      approval({ id: 'a1', entryId: 'a1', dlqMessageId: 41, since: hoursAgo(3) }),
      approval({ id: 'a2', entryId: 'a2', dlqMessageId: 43, componentKey: 'storage-queues', since: hoursAgo(30) }),
      handOff({ id: 'h1', entryId: 'h1', dlqMessageId: 42 }),
    ]))
  })

  it('asks for its own cloud, and for every cloud when the scope is all', async () => {
    open('/next/azure/approvals')
    await screen.findByRole('heading', { level: 1, name: /Pending approvals/ })
    await waitFor(() => expect(pendingApi.fetchPendingWork).toHaveBeenCalledWith({ provider: 'azure' }))
    open('/next/all/approvals')
    await waitFor(() => expect(pendingApi.fetchPendingWork).toHaveBeenCalledWith({}))
  })

  it('lists approvals only: a hand-off is not something to approve', async () => {
    open('/next/azure/approvals')
    expect(await screen.findByRole('heading', { name: 'Waiting for approval (2)' })).toBeInTheDocument()
    expect(screen.getByText('#41')).toBeInTheDocument()
    expect(screen.queryByText('#42')).not.toBeInTheDocument()
    expect(screen.getByText('Approving needs the Approver role.').parentElement).toHaveTextContent('You have it')
  })

  it('opens the Review dialog for one row, and for exactly the ticked rows', async () => {
    const user = userEvent.setup()
    const { router } = open('/next/azure/approvals')
    await user.click(await screen.findByLabelText('Select message 41'))
    await user.click(screen.getByLabelText('Select message 43'))
    await user.click(screen.getByRole('link', { name: 'Review 2 selected…' }))
    expect(router.state.location.search).toBe('?dialog=review&entries=a1,a2')
    await router.navigate('/next/azure/approvals')
    await user.click((await screen.findAllByRole('link', { name: 'Review' }))[0])
    expect(router.state.location.search).toBe('?dialog=review&entry=a1')
  })

  it('shows only the ticked replays in the dialog, and sends nothing until a person approves', async () => {
    open('/next/azure/approvals?dialog=review&entries=a2')
    const dialog = await screen.findByRole('dialog')
    expect(await within(dialog).findByRole('button', { name: /^Approve/ })).toBeInTheDocument()
    expect(within(dialog).getByText(/1 replay waiting/)).toBeInTheDocument()
    expect(pendingApi.approvePending).not.toHaveBeenCalled()
  })

  it('says "Needs an Approver" and offers no review to someone who cannot approve', async () => {
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me({ governanceActive: true, effectiveRole: 'Operator', recoverRole: 'Operator' }))
    open('/next/azure/approvals')
    expect((await screen.findAllByText('Needs an Approver')).length).toBe(2)
    expect(screen.queryByRole('link', { name: 'Review' })).not.toBeInTheDocument()
    expect(screen.getByText('Approving needs the Approver role.').parentElement).toHaveTextContent('You do not have it')
  })

  it('says so when nothing waits, and shows an error rather than an empty list when the read fails', async () => {
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValueOnce(page([]))
    open('/next/azure/approvals')
    expect(await screen.findByText('Nothing is waiting for approval right now.')).toBeInTheDocument()
    vi.mocked(pendingApi.fetchPendingWork).mockRejectedValue(new Error('boom'))
    open('/next/aws/approvals')
    expect(await screen.findByRole('alert')).toHaveTextContent('could not be read')
  })

  it('has no accessibility violations', async () => {
    const { container } = open('/next/azure/approvals')
    await screen.findByRole('table')
    await expectNoAxeViolations(container)
  })
})
