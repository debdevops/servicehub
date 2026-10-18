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
import * as signaturesApi from '@/lib/api/signatures'
import { nextRoute } from '@/next/router'
import { expectNoAxeViolations } from '@tests/support/axe'
import { agent, component, coverageRows, entity, me, page } from './homeData'

vi.mock('@/lib/api/coverage', async (o) => ({ ...(await o<typeof coverageApi>()), fetchCoverage: vi.fn() }))
vi.mock('@/lib/api/components', async (o) => ({ ...(await o<typeof componentsApi>()), fetchComponents: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn() }))
vi.mock('@/lib/api/pendingWork', async (o) => ({ ...(await o<typeof pendingApi>()), fetchPendingWork: vi.fn() }))
vi.mock('@/lib/api/signatures', async (o) => ({ ...(await o<typeof signaturesApi>()), fetchSignatures: vi.fn(), fetchSignatureTrust: vi.fn() }))

const at = '2026-10-18T10:00:00Z'
const trust = (over: Partial<signaturesApi.SignatureTrust> = {}): signaturesApi.SignatureTrust => ({
  level: 'approve', sampleSize: 0, verifiedSuccessRate: null, recovered: 0, returned: 0, failed: 0, unverified: 0, nextLevel: null, moreVerifiedNeeded: 10, rateNeeded: 0.95,
  cloudCanConfirm: true, productionCeiling: false, reasons: [], ...over,
})
const sig = (over: Partial<signaturesApi.Signature> = {}): signaturesApi.Signature => ({
  signatureHash: 'h1', provider: 'azure', reason: 'LockLost', exampleError: 'The lock was lost', entities: ['orders-queue'], messages: 6, activeNow: 4, firstSeenAt: at, lastSeenAt: at, daily: [0, 1, 5],
  growing: false, replays: { replayed: 18, stayedFixed: 18, returned: 0, unverified: 0 }, replayVerdict: 'helps',
  namespaces: [{ id: 'ns-azure', name: 'orders-sb', displayName: null, environment: 'dev', messages: 6 }], ...over,
})
const result = (items: signaturesApi.Signature[]): signaturesApi.SignaturePage => ({ items, total: items.length, page: 1, pageSize: 25, all: 7, growing: 1, replayHelps: 3, replayDoesNotHelp: 2 })
const lastQuery = () => vi.mocked(signaturesApi.fetchSignatures).mock.calls.at(-1)?.[0]

function open(url: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter([nextRoute], { initialEntries: [url] })
  return { router, ...render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>) }
}

describe('Failure signatures (unit E6)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [component({ entities: [entity()] })], generatedAtUtc: at })
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([]))
    vi.mocked(signaturesApi.fetchSignatures).mockResolvedValue(result([sig(), sig({ signatureHash: 'h2', reason: 'Timeout', replays: { replayed: 0, stayedFixed: 0, returned: 0, unverified: 0 }, replayVerdict: 'unknown', growing: true })]))
    vi.mocked(signaturesApi.fetchSignatureTrust).mockImplementation(async (hash) => (hash === 'h1' ? trust({ level: 'standing', sampleSize: 18, recovered: 18, verifiedSuccessRate: 1 }) : trust()))
  })

  it('asks for its own cloud over the last 7 days, 25 a page; for every cloud when the scope is all', async () => {
    open('/next/azure/signatures')
    await screen.findByRole('heading', { level: 1, name: 'Failure signatures' })
    await waitFor(() => expect(lastQuery()).toMatchObject({ provider: 'azure', days: 7, tab: 'all', page: 1, pageSize: 25 }))
    open('/next/all/signatures')
    await waitFor(() => expect(lastQuery()?.provider).toBeUndefined())
  })

  it('shows the server’s own totals in the tiles, never a count it did not read', async () => {
    open('/next/azure/signatures')
    const tile = async (t: string) => (await screen.findByText(t, { selector: 'p' })).parentElement!
    expect(await tile('Signatures')).toHaveTextContent('7')
    expect(await tile('Growing')).toHaveTextContent('1')
    expect(await tile('Replay helps')).toHaveTextContent('3')
  })

  it('says, per row, what replay did, the trust level, and whether the Agent may act — from the trust report', async () => {
    open('/next/azure/signatures')
    const table = await screen.findByRole('table', { name: 'Failure signatures' })
    await waitFor(() => expect(within(table).getAllByRole('row')[1]).toHaveTextContent('L4 · Standing'))
    const rows = within(table).getAllByRole('row')
    expect(rows[1]).toHaveTextContent('18 of 18 stayed fixed')
    expect(rows[1]).toHaveTextContent('Yes')
    expect(rows[1]).toHaveTextContent('orders-sb')
    expect(rows[2]).toHaveTextContent('not replayed')
    await waitFor(() => expect(rows[2]).toHaveTextContent('L3 · Asks first'))
    expect(rows[2]).toHaveTextContent('Not yet')
    expect(rows[2]).toHaveTextContent('Growing')
  })

  it('keeps the tab and the filters in the address and asks the server for them', async () => {
    const user = userEvent.setup()
    const { router } = open('/next/azure/signatures')
    await user.click(await screen.findByRole('tab', { name: /Growing/ }))
    await waitFor(() => expect(lastQuery()).toMatchObject({ tab: 'growing' }))
    expect(router.state.location.search).toContain('tab=growing')
    await user.selectOptions(screen.getByLabelText('Window:'), 'last 30 days')
    await waitFor(() => expect(lastQuery()).toMatchObject({ days: 30 }))
    await user.selectOptions(screen.getByLabelText('Connection:'), 'orders-sb')
    await waitFor(() => expect(lastQuery()).toMatchObject({ namespaceId: 'ns-azure' }))
    await user.click(screen.getByRole('button', { name: 'Clear filters' }))
    await waitFor(() => expect(lastQuery()?.namespaceId).toBeUndefined())
  })

  it('opens a signature’s detail with this shell’s addresses, and no Incident tab until the ledger exists', async () => {
    const user = userEvent.setup()
    open('/next/azure/signatures')
    await user.click(await screen.findByRole('button', { name: 'View LockLost' }))
    const detail = await screen.findByRole('complementary', { name: 'Signature details' })
    expect(within(detail).queryByRole('tab', { name: /Incident/ })).not.toBeInTheDocument()
    expect(within(detail).getByRole('link', { name: /See the 6 messages/ })).toHaveAttribute('href', '/next/azure/c/service-bus/ns-azure?tab=dead-letters&reason=LockLost')
    expect(within(detail).getByRole('link', { name: /Create an auto-replay rule/ })).toHaveAttribute('href', '/next/azure/rules?rule=h1')
    expect(within(detail).getByRole('link', { name: /Ledger entries/ })).toHaveAttribute('href', '/next/azure/ledger')
  })

  it('shows an error, not an empty table, when the read fails; and says so when none was recorded', async () => {
    vi.mocked(signaturesApi.fetchSignatures).mockResolvedValueOnce(result([]))
    open('/next/azure/signatures')
    expect(await screen.findByText(/No failure has been recorded yet/)).toBeInTheDocument()
    vi.mocked(signaturesApi.fetchSignatures).mockRejectedValue(new Error('boom'))
    open('/next/aws/signatures')
    expect(await screen.findByRole('alert')).toHaveTextContent('could not be read')
  })

  it('adds a Cloud column for all clouds', async () => {
    open('/next/all/signatures')
    expect(await screen.findByRole('columnheader', { name: 'Cloud' })).toBeInTheDocument()
  })

  it('has no accessibility violations', async () => {
    const { container } = open('/next/azure/signatures')
    await screen.findByRole('table', { name: 'Failure signatures' })
    await expectNoAxeViolations(container)
  })
})
