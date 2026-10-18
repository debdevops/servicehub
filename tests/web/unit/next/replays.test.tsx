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
import * as recoveryApi from '@/lib/api/recovery'
import * as replayApi from '@/lib/api/replay'
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
vi.mock('@/lib/api/namespaces', async (o) => ({ ...(await o<typeof namespacesApi>()), fetchNamespaces: vi.fn() }))
vi.mock('@/lib/api/replay', async (o) => ({ ...(await o<typeof replayApi>()), fetchReplays: vi.fn() }))
vi.mock('@/lib/api/recovery', async (o) => ({ ...(await o<typeof recoveryApi>()), fetchRecoverySummary: vi.fn() }))

const at = '2026-10-18T10:00:00Z'
const replay = (id: number, status: string, over: Partial<replayApi.ReplayListItem> = {}) => ({
  id, dlqMessageId: id + 100, namespaceId: 'ns-azure', provider: 'azure', messageId: `m-${id}`, sourceEntity: 'orders-queue', targetEntity: 'orders-queue', replayedAt: at, replayedBy: 'maya',
  actor: { identity: 'maya', kind: 'user', label: 'Maya (Approver)', isSession: false }, outcomeStatus: 'accepted', entryState: null, observationWindowEndsAt: null, markerApplied: true,
  verification: { status, reasonCode: null, confidence: null, watchUntil: null, canConfirm: true, remedy: null }, ...over,
}) as replayApi.ReplayListItem
const summary = (counts: Partial<Record<recoveryApi.EntryState, number>>): recoveryApi.RecoverySummary => ({
  window: '7d', total: Object.values(counts).reduce((a, b) => a + (b ?? 0), 0), byProvider: [], stayedFixedRate: null, returnedConfidence: { exact: 0, heuristic: 0 }, replaysAccepted: 0,
  states: Object.entries(counts).map(([state, count]) => ({ state: state as recoveryApi.EntryState, count: count ?? 0 })),
})
const lastReplays = () => vi.mocked(replayApi.fetchReplays).mock.calls.at(-1)?.[0]
const lastSummary = () => vi.mocked(recoveryApi.fetchRecoverySummary).mock.calls.at(-1)?.[0]

function open(url: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter([nextRoute], { initialEntries: [url] })
  return { router, ...render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>) }
}

describe('Replay history (unit E4)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [component()], generatedAtUtc: at })
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([]))
    vi.mocked(namespacesApi.fetchNamespaces).mockResolvedValue([{ id: 'ns-azure', name: 'orders-sb', displayName: null, provider: 'azure' } as namespacesApi.Namespace])
    vi.mocked(replayApi.fetchReplays).mockResolvedValue({ items: [replay(1, 'verified'), replay(2, 'returned'), replay(3, 'verification_required')], total: 3, page: 1, pageSize: 25 })
    vi.mocked(recoveryApi.fetchRecoverySummary).mockResolvedValue(summary({ Recovered: 1, Returned: 1, Unverified: 1 }))
  })

  it('asks for its own cloud over the last 7 days, 25 a page; for every cloud when the scope is all', async () => {
    open('/next/azure/replays')
    await screen.findByRole('heading', { level: 1, name: 'Replay history' })
    await waitFor(() => expect(lastReplays()).toMatchObject({ provider: 'azure', window: '7d', page: 1, pageSize: 25 }))
    expect(lastSummary()).toMatchObject({ provider: 'azure', window: '7d' })
    open('/next/all/replays')
    await waitFor(() => expect(lastReplays()?.provider).toBeUndefined())
  })

  it('shows the five outcome tiles from the one recovery summary, and the outcome in the product’s words', async () => {
    open('/next/azure/replays')
    const tile = async (title: string) => (await screen.findByText(title, { selector: 'p' })).parentElement!
    await waitFor(async () => expect(await tile('Replays')).toHaveTextContent('3')) // "…" until the summary arrives
    expect(await tile('Verified')).toHaveTextContent('1')
    expect(await tile('Came back')).toHaveTextContent('1')
    expect(await tile('Unverified')).toHaveTextContent('1')
    const rows = within(await screen.findByRole('table', { name: 'Messages put back' })).getAllByRole('row')
    expect(rows[1]).toHaveTextContent('Verified')
    expect(rows[2]).toHaveTextContent('Came back')
    expect(rows[3]).toHaveTextContent('Unverified')
    expect(rows[1]).toHaveTextContent('orders-sb') // the connection it was replayed on
  })

  it('keeps the filters in the address and asks the server for them', async () => {
    const user = userEvent.setup()
    const { router } = open('/next/azure/replays')
    await screen.findByRole('table', { name: 'Messages put back' })
    await user.selectOptions(screen.getByLabelText('Outcome:'), 'Came back')
    await waitFor(() => expect(lastReplays()).toMatchObject({ ending: 'returned' }))
    expect(router.state.location.search).toContain('ending=returned')
    await user.selectOptions(screen.getByLabelText('Replayed by:'), 'the Agent')
    await waitFor(() => expect(lastReplays()).toMatchObject({ by: 'autonomous' }))
    expect(screen.getByText(/tiles count everyone/)).toBeInTheDocument()
    await user.selectOptions(screen.getByLabelText('Connection:'), 'orders-sb')
    await waitFor(() => expect(lastReplays()).toMatchObject({ namespaceId: 'ns-azure' }))
    expect(lastSummary()).toMatchObject({ namespaceId: 'ns-azure' })
    await user.click(screen.getByRole('button', { name: 'Clear filters' }))
    await waitFor(() => expect(lastReplays()?.ending).toBeUndefined())
  })

  it('narrows to one Auto Replay rule from the address, and says so', async () => {
    open('/next/azure/replays?rule=7')
    expect(await screen.findByText('Only what one Auto Replay rule sent.')).toBeInTheDocument()
    await waitFor(() => expect(lastReplays()).toMatchObject({ ruleId: 7 }))
  })

  it('says so, and draws no outcome zeros, when nothing was replayed', async () => {
    vi.mocked(replayApi.fetchReplays).mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 25 })
    vi.mocked(recoveryApi.fetchRecoverySummary).mockResolvedValue(summary({}))
    open('/next/aws/replays')
    expect(await screen.findByText(/Nothing has been replayed on AWS/)).toBeInTheDocument()
    expect(screen.queryByText('Came back', { selector: 'p' })).not.toBeInTheDocument()
  })

  it('shows an error, not an empty table, when the read fails', async () => {
    vi.mocked(replayApi.fetchReplays).mockRejectedValue(new Error('boom'))
    open('/next/azure/replays')
    expect(await screen.findByRole('alert')).toHaveTextContent('could not be read')
  })

  it('opens the Message dialog from a row', async () => {
    const user = userEvent.setup()
    const { router } = open('/next/azure/replays')
    await user.click((await screen.findAllByRole('link', { name: /Open message m-1/ }))[0])
    expect(router.state.location.search).toBe('?dialog=message&id=101')
  })

  it('has no accessibility violations', async () => {
    const { container } = open('/next/azure/replays')
    await screen.findByRole('table', { name: 'Messages put back' })
    await expectNoAxeViolations(container)
  })
})
