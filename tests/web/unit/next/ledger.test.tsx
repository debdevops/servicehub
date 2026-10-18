import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as agentsApi from '@/lib/api/agents'
import * as componentsApi from '@/lib/api/components'
import * as coverageApi from '@/lib/api/coverage'
import * as deadLettersApi from '@/lib/api/deadLetters'
import * as identityApi from '@/lib/api/identity'
import * as pendingApi from '@/lib/api/pendingWork'
import * as recoveryApi from '@/lib/api/recovery'
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
vi.mock('@/lib/api/deadLetters', async (o) => ({ ...(await o<typeof deadLettersApi>()), fetchDeadLetter: vi.fn() }))
vi.mock('@/lib/api/recovery', async (o) => ({
  ...(await o<typeof recoveryApi>()), fetchLedger: vi.fn(), fetchLedgerEntry: vi.fn(), fetchRecoverySummary: vi.fn(), verifyChain: vi.fn(), exportEvidence: vi.fn(),
}))

const at = '2026-10-18T10:00:00Z'
const entryRow = (id: string, state: recoveryApi.EntryState, over: Partial<recoveryApi.LedgerEntry> = {}): recoveryApi.LedgerEntry => ({
  id, operationId: `op-${id}`, begunAt: at, kind: 'Replay', entityName: 'orders-queue', targetEntity: 'orders-queue', provider: 'azure', namespaceName: 'orders-sb',
  actor: { identity: 'maya', kind: 'user', label: 'Maya (Approver)', isSession: false }, state, confidence: null, dlqMessageId: 101, closedAt: null, messageId: `m-${id}`, level: 'approve', ...over,
})
const summary = (counts: Partial<Record<recoveryApi.EntryState, number>>): recoveryApi.RecoverySummary => ({
  window: '7d', total: Object.values(counts).reduce((a, b) => a + (b ?? 0), 0), byProvider: [], stayedFixedRate: null, returnedConfidence: { exact: 0, heuristic: 0 }, replaysAccepted: 0,
  states: Object.entries(counts).map(([state, count]) => ({ state: state as recoveryApi.EntryState, count: count ?? 0 })),
})
const lastLedger = () => vi.mocked(recoveryApi.fetchLedger).mock.calls.at(-1)?.[0]

function open(url: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter([nextRoute], { initialEntries: [url] })
  return { router, ...render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>) }
}

describe('Evidence ledger (unit E7)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [component()], generatedAtUtc: at })
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([approval(), handOff()]))
    vi.mocked(deadLettersApi.fetchDeadLetter).mockRejectedValue(new Error('not needed'))
    vi.mocked(recoveryApi.fetchRecoverySummary).mockResolvedValue(summary({ Recovered: 1, Returned: 1, Observing: 2 }))
    vi.mocked(recoveryApi.fetchLedger).mockResolvedValue({ items: [entryRow('e1', 'Recovered'), entryRow('e2', 'Returned', { confidence: 'Exact' })], total: 2, page: 1, pageSize: 25 })
    vi.mocked(recoveryApi.fetchLedgerEntry).mockResolvedValue({ entry: entryRow('e1', 'Recovered'), recoveryMarker: 'rm-1', markerApplied: true, deadLetterReason: null, verificationResult: null, observationWindowEndsAt: null, lastEventSeq: 1, events: [] })
    vi.mocked(recoveryApi.verifyChain).mockResolvedValue({ ownerId: 'o', isValid: true, eventsChecked: 42, firstDivergentSeq: null, reason: null })
  })

  it('asks for its own cloud over the last 7 days, 25 a page; for every cloud when the scope is all', async () => {
    open('/next/azure/ledger')
    await screen.findByRole('heading', { level: 1, name: 'Evidence ledger' })
    await waitFor(() => expect(lastLedger()).toMatchObject({ provider: 'azure', window: '7d', page: 1, pageSize: 25 }))
    open('/next/all/ledger')
    await waitFor(() => expect(lastLedger()?.provider).toBeUndefined())
  })

  it('words each state from what the ledger proved — Recovered is “did not return”, never “succeeded”', async () => {
    open('/next/azure/ledger')
    const table = await screen.findByRole('table', { name: /Recovery ledger entries/ })
    const rows = within(table).getAllByRole('row')
    expect(rows[1]).toHaveTextContent('Recovered')
    expect(rows[1]).toHaveTextContent('Did not return')
    expect(rows[1]).not.toHaveTextContent(/succe/i)
    expect(rows[2]).toHaveTextContent('Returned')
    expect(rows[2]).toHaveTextContent('Recognised by its recovery ID')
  })

  it('counts the tabs from the summary and the pending-work read, and Watching asks for that state', async () => {
    const user = userEvent.setup()
    const { router } = open('/next/azure/ledger')
    expect(await screen.findByRole('tab', { name: 'All (4)' })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Watching (2)' })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Waiting (2)' })).toBeInTheDocument()
    await user.click(screen.getByRole('tab', { name: /Watching/ }))
    await waitFor(() => expect(lastLedger()).toMatchObject({ state: 'Observing' }))
    expect(router.state.location.search).toContain('tab=watching')
  })

  it('lists what waits for a person, with Review or View in this shell’s dialogs', async () => {
    const user = userEvent.setup()
    open('/next/azure/ledger?tab=waiting')
    const table = await screen.findByRole('table', { name: 'Entries waiting for a person' })
    expect(within(table).getByRole('link', { name: 'Review' })).toHaveAttribute('href', expect.stringContaining('dialog=review&entry=e1'))
    expect(within(table).getByRole('link', { name: 'View' })).toHaveAttribute('href', expect.stringContaining('panel=hand-off&entry=e2'))
    await user.click(screen.getByRole('tab', { name: /All/ }))
  })

  it('keeps the filters in the address and asks the server for them', async () => {
    const user = userEvent.setup()
    const { router } = open('/next/azure/ledger')
    await screen.findByRole('table', { name: /Recovery ledger entries/ })
    await user.selectOptions(screen.getByLabelText('State:'), 'Returned')
    await waitFor(() => expect(lastLedger()).toMatchObject({ state: 'Returned' }))
    await user.selectOptions(screen.getByLabelText('Who:'), 'the Agent')
    await waitFor(() => expect(lastLedger()).toMatchObject({ by: 'autonomous' }))
    await user.selectOptions(screen.getByLabelText('Connection:'), 'orders-sb')
    await waitFor(() => expect(lastLedger()).toMatchObject({ namespaceId: 'ns-azure' }))
    expect(router.state.location.search).toContain('state=Returned')
    await user.click(screen.getByRole('button', { name: 'Clear filters' }))
    await waitFor(() => expect(lastLedger()?.state).toBeUndefined())
  })

  it('opens an entry beside the table, with a message link and a Replay link in this shell, only for a message still in dead-letter', async () => {
    const user = userEvent.setup()
    vi.mocked(deadLettersApi.fetchDeadLetter).mockResolvedValue({ item: { id: 101, status: 'active', messageId: 'm-e1', deadLetterReason: null, deadLetterErrorDescription: null } } as never)
    open('/next/azure/ledger')
    await user.click((await screen.findAllByRole('button', { name: /Open ledger entry/ }))[0])
    const panel = await screen.findByRole('complementary', { name: 'Entry' })
    expect(within(panel).getByRole('link', { name: 'Open the message →' })).toHaveAttribute('href', expect.stringContaining('dialog=message&id=101'))
    expect(await within(panel).findByRole('link', { name: 'Replay…' })).toHaveAttribute('href', expect.stringContaining('dialog=replay&id=101'))
  })

  it('checks the chain only when asked, and says what it found', async () => {
    const user = userEvent.setup()
    open('/next/azure/ledger')
    await screen.findByRole('table', { name: /Recovery ledger entries/ })
    expect(recoveryApi.verifyChain).not.toHaveBeenCalled()
    await user.click(screen.getByRole('button', { name: 'Check the chain' }))
    expect((await screen.findByText('Chain verified')).parentElement).toHaveTextContent('42 events')
  })

  it('says so when nothing was recorded, and shows an error rather than an empty table when the read fails', async () => {
    vi.mocked(recoveryApi.fetchLedger).mockResolvedValueOnce({ items: [], total: 0, page: 1, pageSize: 25 })
    open('/next/azure/ledger')
    expect(await screen.findByText(/No recovery action in this period/)).toBeInTheDocument()
    vi.mocked(recoveryApi.fetchLedger).mockRejectedValue(new Error('boom'))
    open('/next/aws/ledger')
    expect(await screen.findByRole('alert')).toHaveTextContent('could not be read')
  })

  it('has no accessibility violations', async () => {
    const { container } = open('/next/azure/ledger')
    await screen.findByRole('table', { name: /Recovery ledger entries/ })
    await expectNoAxeViolations(container)
  })
})
