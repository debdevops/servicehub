import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as agents from '../../lib/api/agents'
import * as insights from '../../lib/api/insights'
import * as ns from '../../lib/api/namespaces'
import * as pending from '../../lib/api/pendingWork'
import * as recovery from '../../lib/api/recovery'
import * as signatures from '../../lib/api/signatures'
import AdvancedOverviewPage from './AdvancedOverviewPage'
import { expectNoAxeViolations } from '../../test/axe'

vi.mock('../../lib/api/agents')
vi.mock('../../lib/api/insights')
vi.mock('../../lib/api/namespaces')
vi.mock('../../lib/api/pendingWork')
vi.mock('../../lib/api/recovery')
vi.mock('../../lib/api/signatures')

const states = (o: Partial<Record<recovery.EntryState, number>>) =>
  (['Executing', 'Observing', 'ExecutionFailed', 'ExecutionUnknown', 'Recovered', 'Returned', 'Discarded', 'Unverified', 'WrittenOff', 'Expired', 'Declined'] as const).map((state) => ({ state, count: o[state] ?? 0 }))

function wrap(url = '/advanced') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}><MemoryRouter initialEntries={[url]}><AdvancedOverviewPage /></MemoryRouter></QueryClientProvider>)
}

describe('Advanced Overview', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(ns.fetchNamespaces).mockResolvedValue([
      { id: 'a', name: 'a', provider: 'azure', environment: 'dev', capabilities: { canProveDlqAbsence: true } },
      { id: 'w', name: 'w', provider: 'aws', environment: 'dev', capabilities: { canProveDlqAbsence: false } },
    ] as unknown as ns.Namespace[])
    vi.mocked(recovery.fetchRecoverySummary).mockResolvedValue({ window: '24h', total: 23, states: states({ Recovered: 18, Unverified: 4, Returned: 1 }), byProvider: [], stayedFixedRate: 0.95, returnedConfidence: { exact: 1, heuristic: 0 }, replaysAccepted: 23 })
    vi.mocked(pending.fetchPendingWork).mockResolvedValue({ items: [{ kind: 'approval', id: '1', entryId: 'e', agentId: null, dlqMessageId: 1, namespaceId: 'w', namespaceName: 'w', provider: 'aws', environment: 'dev', entity: 'orders-sqs', deadLetterReason: null, ruleId: null, ruleName: null, reasonCode: 'X', reason: 'the Agent stopped and asked', since: '2026-09-26T10:00:00Z' }], total: 1, byProvider: [], agents: 0 })
    vi.mocked(agents.fetchAgents).mockResolvedValue([{ id: 'bulk-replay', canAct: true, isPaused: false, health: 'healthy', late: false, name: 'Bulk' }, { id: 'dlq-monitor', canAct: false, isPaused: false, health: 'healthy', late: false, name: 'Monitor' }] as unknown as agents.Agent[])
    vi.mocked(agents.fetchAgentActivity).mockResolvedValue({ agentId: 'autonomy-evaluation', cyclesSinceUtc: null, items: [{ at: '2026-09-26T09:00:00Z', source: 'ledger', kind: 'AutonomyGranted', text: 'payments-timeout earned standing permission', by: null }] })
    vi.mocked(signatures.fetchAuthority).mockResolvedValue({ total: 14, capped: false, unattended: 0, standing: 2, approve: 12, held: [{ reason: 'cannot_verify', count: 8 }, { reason: 'needs_evidence', count: 4 }], needs: { sample: 10, rate: 0.95 } })
  })

  it('has no accessibility violations (6.6)', async () => {
    const { container } = wrap()
    await screen.findByText('95% stayed fixed')
    await expectNoAxeViolations(container)
  })

  it('leads with what needs a person, as a link', async () => {
    wrap()
    const attention = await screen.findByRole('region', { name: 'Needs your attention' })
    expect(within(attention).getByText('approval waiting')).toBeInTheDocument()
    expect(within(attention).getByRole('link', { name: /See them waiting/ })).toHaveAttribute('href', '/advanced/ledger?state=Waiting')
    expect(screen.queryByRole('button', { name: /replay|approve/i })).not.toBeInTheDocument()
  })

  it('breaks recovery down — Recovered and Unverified never merged — each state linking to the Ledger, with Simple’s stayed-fixed figure', async () => {
    wrap()
    const rec = await screen.findByRole('region', { name: 'Recovery' })
    expect(await within(rec).findByText('95% stayed fixed')).toBeInTheDocument()
    expect(within(rec).getByRole('link', { name: 'Recovered' })).toHaveAttribute('href', '/advanced/ledger?state=Recovered&window=24h')
    expect(within(rec).getByRole('link', { name: 'Unverified' })).toBeInTheDocument()
  })

  it('says why the floor’s population is held there, and draws the floor as a floor', async () => {
    wrap()
    const auth = await screen.findByRole('region', { name: 'Authority — and why' })
    expect(await within(auth).findByText(/the cloud can’t verify the queue drained/)).toBeInTheDocument()
    expect(within(auth).getByText(/10 at 95% to move up/)).toBeInTheDocument()
    expect(within(auth).getByText(/a floor — not a stage/i)).toBeInTheDocument()
  })

  it('shows each cloud’s capability and the recorded autonomy transitions', async () => {
    wrap()
    const cap = await screen.findByRole('region', { name: 'Capability' })
    expect(await within(cap).findByText('✓ Can verify DLQ drain')).toBeInTheDocument()
    expect(within(cap).getByText(/Can’t prove a fix held/)).toBeInTheDocument()
    expect(await screen.findByText('payments-timeout earned standing permission')).toBeInTheDocument()
  })

  it('Insights lists findings with their evidence, and marks summaries as suggestions', async () => {
    const user = userEvent.setup()
    vi.mocked(insights.fetchInsights).mockResolvedValue({ lastLookedAt: '2026-09-26T10:00:00Z', cleared: [], current: [
      { id: 1, kind: 'anomaly', severity: 80, what: "Entity 'orders' had 40 dead-lettered message(s)… a spike", entityName: 'orders', namespaceId: 'a', namespaceName: 'a', provider: 'azure', metrics: { currentCount: 40 }, firstSeenAt: '2026-09-26T09:00:00Z', lastSeenAt: '2026-09-26T10:00:00Z', clearedAt: null, suggestion: false },
      { id: 2, kind: 'narration', severity: 60, what: 'Unusual activity in a.', entityName: null, namespaceId: 'a', namespaceName: 'a', provider: 'azure', metrics: null, firstSeenAt: '2026-09-26T09:00:00Z', lastSeenAt: '2026-09-26T10:00:00Z', clearedAt: null, suggestion: true },
    ] })
    wrap()
    await user.click(await screen.findByRole('button', { name: 'Insights' }))
    const list = await screen.findByRole('list', { name: 'Current insights' })
    expect(within(list).getByText(/a spike/)).toBeInTheDocument()
    expect(within(list).getByRole('link', { name: /See the dead letters/ })).toHaveAttribute('href', '/?tab=dlq&ns=a&entity=orders')
    expect(within(list).getAllByText('Suggestion')).toHaveLength(1)
  })
})
