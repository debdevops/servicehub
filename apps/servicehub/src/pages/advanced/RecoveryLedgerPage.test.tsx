import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as nsApi from '../../lib/api/namespaces'
import * as api from '../../lib/api/recovery'
import type { LedgerEntry, LedgerEntryDetail, RecoverySummary, StateCount } from '../../lib/api/recovery'
import RecoveryLedgerPage from './RecoveryLedgerPage'

vi.mock('../../lib/api/namespaces')
vi.mock('../../lib/api/recovery')

const entry = (id: string, state: LedgerEntry['state'], over: Partial<LedgerEntry> = {}): LedgerEntry => ({
  id, operationId: 'op', beganAt: '2026-09-25T10:00:00Z', kind: 'Replay', entityName: 'payments-dlq', targetEntity: 'payments', provider: 'azure', namespaceName: 'orders-dev',
  actor: { identity: 'session', kind: 'user', label: 'from this browser session', isSession: true }, state, confidence: null, dlqMessageId: 7, closedAt: null, ...over,
})

const states = (over: Record<string, number> = {}): StateCount[] =>
  (['Executing', 'Observing', 'ExecutionFailed', 'ExecutionUnknown', 'Recovered', 'Returned', 'Discarded', 'Unverified', 'WrittenOff', 'Expired', 'Declined'] as const).map((s) => ({ state: s, count: over[s] ?? 0 }))

const summary = (over: Record<string, number> = {}): RecoverySummary => ({
  window: '24h', total: Object.values(over).reduce((a, b) => a + b, 0), states: states(over), byProvider: [], stayedFixedRate: null, returnedConfidence: { exact: 0, heuristic: 0 }, replaysAccepted: 0,
})

const detail = (e: LedgerEntry): LedgerEntryDetail => ({
  entry: e, recoveryMarker: 'rcv-1', markerApplied: true, deadLetterReason: null, verificationResult: null, observationWindowEndsAt: null, lastEventSeq: 3,
  events: [
    { seq: 1, eventType: 'EntryBegun', occurredAt: '2026-09-25T10:00:00Z', actor: e.actor, detail: null, prevHash: '0'.repeat(64), entryHash: 'a'.repeat(64) },
    { seq: 2, eventType: 'ProviderAccepted', occurredAt: '2026-09-25T10:00:01Z', actor: e.actor, detail: null, prevHash: 'a'.repeat(64), entryHash: 'b'.repeat(64) },
  ],
})

function Where() {
  const { search } = useLocation()
  return <output data-testid="where">{search}</output>
}

function renderPage(url = '/advanced/ledger') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[url]}><Where /><RecoveryLedgerPage /></MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('the Recovery Ledger (Advanced)', () => {
  beforeEach(() => {
    vi.mocked(nsApi.fetchNamespaces).mockResolvedValue([{ id: 'n', name: 'orders-dev', provider: 'azure' }] as never)
    vi.mocked(api.fetchRecoverySummary).mockResolvedValue(summary({ Recovered: 2, Unverified: 1 }))
    vi.mocked(api.fetchLedger).mockResolvedValue({ items: [entry('e1', 'Recovered'), entry('e2', 'Unverified')], total: 2, page: 1, pageSize: 10 })
  })

  it('shows every state in the tabs with its count — zeros included — and never merges Recovered with Unverified', async () => {
    renderPage()
    const nav = within(await screen.findByRole('navigation', { name: 'Outcome' }))
    expect(await nav.findByRole('button', { name: /Recovered\s*2/ })).toBeInTheDocument()
    expect(nav.getByRole('button', { name: /Unverified\s*1/ })).toBeInTheDocument()
    expect(nav.getByRole('button', { name: /Failed\s*0/ })).toBeInTheDocument()
  })

  it('states the outcome in the enum’s words in the table', async () => {
    renderPage()
    const table = within(await screen.findByRole('table'))
    expect(table.getByText('Recovered')).toBeInTheDocument()
    expect(table.getByText('Unverified')).toBeInTheDocument()
  })

  it('honours ?state= so the overview can deep-link, and asks the API for exactly that state', async () => {
    renderPage('/advanced/ledger?state=Unverified')
    await screen.findByRole('table')
    expect(vi.mocked(api.fetchLedger)).toHaveBeenCalledWith(expect.objectContaining({ state: 'Unverified' }))
  })

  it('changing a tab puts the state in the URL', async () => {
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByRole('button', { name: /Unverified/ }))
    expect(screen.getByTestId('where').textContent).toContain('state=Unverified')
  })

  it('opens an entry: who, what happened, the evidence — and links out rather than acting', async () => {
    vi.mocked(api.fetchLedgerEntry).mockResolvedValue(detail(entry('e1', 'Recovered')))
    const user = userEvent.setup()
    renderPage()
    const buttons = await screen.findAllByRole('button', { name: /Open ledger entry/ })
    await user.click(buttons[0]!)
    const panel = within(await screen.findByRole('complementary', { name: 'Entry' }))
    expect(await panel.findByText('did not return')).toBeInTheDocument()
    expect(panel.getByText('Replay begun')).toBeInTheDocument()
    expect(panel.getByText(/Sent, and accepted by Azure/)).toBeInTheDocument()
    expect(panel.getByRole('link', { name: /Replay again/ })).toHaveAttribute('href', expect.stringContaining('message=7'))
    // Nothing on this page acts: no replay/write-off/purge button anywhere.
    expect(screen.queryByRole('button', { name: /^(Replay|Write off|Purge|Delete)/i })).toBeNull()
  })

  it('verifies the chain on request and shows the result read-only', async () => {
    vi.mocked(api.fetchLedgerEntry).mockResolvedValue(detail(entry('e1', 'Recovered')))
    vi.mocked(api.verifyChain).mockResolvedValue({ ownerId: 'o', isValid: true, eventsChecked: 5, firstDivergentSeq: null, reason: null })
    const user = userEvent.setup()
    renderPage('/advanced/ledger?entry=e1')
    await user.click(await screen.findByRole('button', { name: /Verify the chain/ }))
    expect(await screen.findByText(/Chain verified/)).toBeInTheDocument()
    expect(screen.getByText(/5 events, unbroken/)).toBeInTheDocument()
  })

  it('says loudly when the chain does not verify', async () => {
    vi.mocked(api.fetchLedgerEntry).mockResolvedValue(detail(entry('e1', 'Recovered')))
    vi.mocked(api.verifyChain).mockResolvedValue({ ownerId: 'o', isValid: false, eventsChecked: 2, firstDivergentSeq: 2, reason: 'EntryHash mismatch' })
    const user = userEvent.setup()
    renderPage('/advanced/ledger?entry=e1')
    await user.click(await screen.findByRole('button', { name: /Verify the chain/ }))
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent(/does not verify/))
    expect(screen.getByRole('alert')).toHaveTextContent(/event 2/)
  })

  it('says so when a window has no entries', async () => {
    vi.mocked(api.fetchLedger).mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 10 })
    renderPage()
    expect(await screen.findByText(/No recovery actions in this window/)).toBeInTheDocument()
  })
})
