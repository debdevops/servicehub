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
vi.mock('@/lib/api/components', async (o) => ({ ...(await o<typeof componentsApi>()), fetchComponent: vi.fn(), fetchComponents: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn() }))
vi.mock('@/lib/api/pendingWork', async (o) => ({ ...(await o<typeof pendingApi>()), fetchPendingWork: vi.fn() }))
vi.mock('@/lib/api/recovery', async (o) => ({ ...(await o<typeof recoveryApi>()), fetchRecoverySummary: vi.fn() }))
vi.mock('@/lib/api/replay', async (o) => ({ ...(await o<typeof replayApi>()), fetchReplays: vi.fn() }))
vi.mock('@/lib/api/namespaces', async (o) => ({ ...(await o<typeof namespacesApi>()), fetchNamespace: vi.fn(), fetchEntities: vi.fn(), fetchNamespaces: vi.fn() }))

const at = '2026-10-18T09:00:00Z'
const ok = '/next/azure/c/service-bus/ns-azure?tab=recovery'
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

function open(url = ok, canProve = true) {
  const c = component()
  vi.mocked(componentsApi.fetchComponent).mockResolvedValue(c)
  vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [c], generatedAtUtc: at })
  vi.mocked(namespacesApi.fetchNamespace).mockResolvedValue({
    id: 'ns-azure', name: 'orders-sb', displayName: null, provider: 'azure', environment: 'dev', authType: 'connectionString', awsRegion: null, gcpProjectId: null,
    capabilities: { supportsMessageCounts: true, supportsRepeatablePeek: true, countsAreApproximate: false, canProveDlqAbsence: canProve },
  } as unknown as namespacesApi.Namespace)
  vi.mocked(namespacesApi.fetchEntities).mockResolvedValue({ namespaceId: 'ns-azure', entities: [] })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter([nextRoute], { initialEntries: [url] })
  render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>)
  return router
}

describe('the Recovery tab (unit D4)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([]))
    vi.mocked(replayApi.fetchReplays).mockResolvedValue({ items: [replay(1, 'verified'), replay(2, 'returned', { verification: { status: 'returned', reasonCode: null, confidence: 'Exact', watchUntil: null, canConfirm: true, remedy: null } })], total: 2, page: 1, pageSize: 25 })
    vi.mocked(recoveryApi.fetchRecoverySummary).mockResolvedValue(summary({ Recovered: 1, Returned: 1 }))
  })

  it('asks for this connection’s replays over the last 7 days, 25 a page, and its outcome summary for the same period', async () => {
    open()
    await screen.findByRole('heading', { name: 'Replays on this component' })
    expect(lastReplays()).toMatchObject({ namespaceId: 'ns-azure', window: '7d', page: 1, pageSize: 25 })
    expect(vi.mocked(recoveryApi.fetchRecoverySummary).mock.calls.at(-1)?.[0]).toMatchObject({ namespaceId: 'ns-azure', window: '7d' })
    expect(screen.getByRole('link', { name: 'Recovery' })).toHaveAttribute('aria-current', 'page')
  })

  it('shows the outcome tiles in the product’s words, from the one recovery summary', async () => {
    vi.mocked(recoveryApi.fetchRecoverySummary).mockResolvedValue(summary({ Recovered: 4, Returned: 1, Observing: 2 }))
    open()
    expect(await screen.findByTestId('tile-verified')).toHaveTextContent('4')
    expect(screen.getByTestId('tile-returned')).toHaveTextContent('1')
    expect(screen.getByTestId('tile-watching')).toHaveTextContent('2')
    expect(screen.getByTestId('tile-replays')).toHaveTextContent('2') // the list's own total for the period
    expect(screen.queryByTestId('tile-unverified')).not.toBeInTheDocument()
  })

  it('on a cloud that cannot prove it, the tile is Unverified and Verified is not offered at all', async () => {
    vi.mocked(recoveryApi.fetchRecoverySummary).mockResolvedValue(summary({ Unverified: 6, Returned: 1 }))
    open(ok, false)
    expect(await screen.findByTestId('tile-unverified')).toHaveTextContent('6')
    expect(screen.queryByTestId('tile-verified')).not.toBeInTheDocument()
    expect(screen.getByText(/ends Unverified, never Verified/)).toBeInTheDocument()
  })

  it('lists each replay with who did it and what was observed, and a Came back says how it was matched', async () => {
    open()
    const table = await screen.findByRole('table', { name: /Replays from Azure-Dev|Replays from orders-sb/ })
    const verified = within(table).getByRole('row', { name: /m-1/ })
    expect(verified).toHaveTextContent('Maya (Approver)')
    expect(within(verified).getByText('Verified')).toBeInTheDocument()
    expect(verified).toHaveTextContent('Did not return in the window')
    const back = within(table).getByRole('row', { name: /m-2/ })
    expect(within(back).getByText('Came back')).toBeInTheDocument()
    expect(back).toHaveTextContent('matched by its recovery ID')
    expect(within(back).getByRole('link', { name: 'Open message m-2' })).toHaveAttribute('href', '/next/azure/c/service-bus/ns-azure?tab=recovery&dialog=message&id=102')
  })

  it('shows a subscription as topic / name, not its stored path', async () => {
    vi.mocked(replayApi.fetchReplays).mockResolvedValue({ items: [replay(1, 'verified', { sourceEntity: 'orders-topic/subscriptions/orders-subscription', targetEntity: 'orders-topic' })], total: 1, page: 1, pageSize: 25 })
    open()
    const row = await screen.findByRole('row', { name: /m-1/ })
    expect(row).toHaveTextContent('orders-topic / orders-subscription')
    expect(row).not.toHaveTextContent('/subscriptions/')
  })

  it('never calls anything "fixed" or "succeeded"', async () => {
    open()
    const table = await screen.findByRole('table')
    expect(table.textContent).not.toMatch(/fixed|succeed/i)
  })

  it('with no replays it says so and draws no outcome tiles that would look like zeros', async () => {
    vi.mocked(replayApi.fetchReplays).mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 25 })
    vi.mocked(recoveryApi.fetchRecoverySummary).mockResolvedValue(summary({}))
    open()
    expect(await screen.findByText(/Nothing has been replayed from .* in this period \(last 7 days\)/)).toBeInTheDocument()
    expect(screen.getByTestId('tile-replays')).toHaveTextContent('0')
    for (const id of ['tile-verified', 'tile-unverified', 'tile-returned', 'tile-watching']) expect(screen.queryByTestId(id)).not.toBeInTheDocument()
  })

  it('period and outcome filters live in the address and ask again from page 1', async () => {
    const router = open(`${ok}&page=3`)
    await screen.findByRole('table')
    await userEvent.selectOptions(screen.getByLabelText('Period'), '30d')
    await waitFor(() => expect(lastReplays()).toMatchObject({ window: '30d', page: 1 }))
    expect(router.state.location.search).toContain('window=30d')
    expect(router.state.location.search).not.toContain('page=')
    await userEvent.selectOptions(screen.getByLabelText('Outcome'), 'returned')
    await waitFor(() => expect(lastReplays()).toMatchObject({ ending: 'returned' }))
    await userEvent.click(screen.getByRole('button', { name: 'Clear filters' }))
    await waitFor(() => expect(router.state.location.search).not.toContain('ending='))
  })

  it('an unrecognised period or outcome in the address falls back to the defaults, never into a query', async () => {
    open(`${ok}&window=forever&ending=banana`)
    await screen.findByRole('table')
    expect(lastReplays()).toMatchObject({ window: '7d' })
    expect(lastReplays()?.ending).toBeUndefined()
  })

  it('a filtered empty list says no replay matches that outcome', async () => {
    vi.mocked(replayApi.fetchReplays).mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 25 })
    open(`${ok}&ending=returned`)
    expect(await screen.findByText('No replay matches that outcome.')).toBeInTheDocument()
  })

  it('pages on the server', async () => {
    vi.mocked(replayApi.fetchReplays).mockResolvedValue({ items: Array.from({ length: 25 }, (_, i) => replay(i + 1, 'verified')), total: 60, page: 1, pageSize: 25 })
    const router = open()
    expect(await screen.findByText('1–25 of 60')).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Pages of replays' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Next page' }))
    await waitFor(() => expect(router.state.location.search).toContain('page=2'))
    await waitFor(() => expect(lastReplays()).toMatchObject({ page: 2 }))
  })

  it('links to Replay history across connections', async () => {
    open()
    expect(await screen.findByRole('link', { name: 'Open Replay history →' })).toHaveAttribute('href', '/next/azure/replays')
  })

  it('says it could not read, rather than showing an empty list that looks like nothing happened', async () => {
    vi.mocked(replayApi.fetchReplays).mockRejectedValue(new Error('down'))
    open()
    expect(await screen.findByText(/What was replayed here could not be read/)).toBeInTheDocument()
    expect(screen.queryByText(/Nothing has been replayed/)).not.toBeInTheDocument()
  })

  it('has no accessibility violations', async () => {
    const { container } = (() => { open(); return { container: document.body } })()
    await screen.findByRole('table')
    await expectNoAxeViolations(container)
  })
})
