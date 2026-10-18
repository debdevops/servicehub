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
import * as bulkApi from '@/lib/api/bulk'
import * as rulesApi from '@/lib/api/rules'
import { bulkSelection } from '@/lib/bulkSelection'
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
vi.mock('@/lib/api/bulk', async (o) => ({ ...(await o<typeof bulkApi>()), previewBulk: vi.fn(), startBulk: vi.fn(), fetchBulk: vi.fn() }))
vi.mock('@/lib/api/rules', async (o) => ({
  ...(await o<typeof rulesApi>()), fetchRules: vi.fn(), fetchRuleMatches: vi.fn(), fetchRuleSources: vi.fn(), generateRules: vi.fn(), setRuleEnabled: vi.fn(), deleteRule: vi.fn(),
}))

const at = '2026-10-18T10:00:00Z'
const rule = (over: Partial<rulesApi.Rule> = {}): rulesApi.Rule => ({
  id: 1, name: 'Order lock losses', provider: 'azure', reason: 'LockLost', entityName: 'orders-queue', signatureHash: 'h1', maxPerHour: 10, waitSeconds: 120, backOff: true, enabled: true,
  disabledReason: null, disabledDetail: null, updatedAt: at, askedCount: 0, askedIsLowerBound: false, lastAskedReason: null, replayed: 18, lastReplayedAt: at, verifiedOutcomes: 18, stayedFixed: 18,
  sampleSize: 10, successFloor: 0.5, ...over,
})
const tripped = rule({ id: 2, name: 'Payment timeouts', reason: 'Timeout', enabled: false, disabledReason: 'CircuitBreaker', disabledDetail: 'Fewer than half stayed fixed.', verifiedOutcomes: 20, stayedFixed: 9 })
const off = rule({ id: 3, name: 'Image retries', reason: null, entityName: null, enabled: false, disabledReason: 'Person', replayed: 0, lastReplayedAt: null, verifiedOutcomes: 0, stayedFixed: 0 })

function open(url: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter([nextRoute], { initialEntries: [url] })
  return { router, ...render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>) }
}

describe('Auto Replay rules (unit E5)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [component()], generatedAtUtc: at })
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([]))
    vi.mocked(namespacesApi.fetchNamespaces).mockResolvedValue([])
    vi.mocked(rulesApi.fetchRules).mockResolvedValue([rule(), tripped, off])
    vi.mocked(rulesApi.fetchRuleMatches).mockResolvedValue([])
    vi.mocked(rulesApi.fetchRuleSources).mockResolvedValue([])
    vi.mocked(rulesApi.generateRules).mockResolvedValue([])
  })

  it('asks for its own cloud’s rules; for all clouds it asks once per connected cloud', async () => {
    open('/next/azure/rules')
    await screen.findByRole('heading', { level: 1, name: 'Auto Replay rules' })
    await waitFor(() => expect(rulesApi.fetchRules).toHaveBeenCalledWith('azure'))
    vi.clearAllMocks()
    vi.mocked(rulesApi.fetchRules).mockResolvedValue([])
    open('/next/all/rules')
    await waitFor(() => expect(rulesApi.fetchRules).toHaveBeenCalledWith('azure'))
    expect(rulesApi.fetchRules).not.toHaveBeenCalledWith('aws')
  })

  it('counts the rules by state and words each state from why the server says it is off', async () => {
    open('/next/azure/rules')
    const table = await screen.findByRole('table', { name: 'Auto Replay rules' })
    const rows = within(table).getAllByRole('row')
    expect(rows[1]).toHaveTextContent('Order lock losses')
    expect(rows[1]).toHaveTextContent('18 of 18 stayed fixed (100%)')
    expect(rows[1]).toHaveTextContent('10 an hour')
    expect(rows[1]).toHaveTextContent('2 min')
    expect(rows[2]).toHaveTextContent('Stopped itself')
    expect(rows[2]).toHaveTextContent('9 of 20 stayed fixed (45%)')
    expect(rows[3]).toHaveTextContent('Any entity')
    expect(rows[3]).toHaveTextContent('Nothing replayed yet')
    expect(screen.getByText('Stopped itself', { selector: 'p' }).parentElement).toHaveTextContent('1')
    expect(screen.getByText('Payment timeouts switched itself off.')).toBeInTheDocument()
  })

  it('opens the rule card under its row, with Replay all disabled while nothing matching is waiting', async () => {
    const user = userEvent.setup()
    open('/next/azure/rules')
    await user.click(await screen.findByRole('button', { name: 'Manage Order lock losses' }))
    expect(await screen.findByRole('switch', { name: 'Order lock losses is on' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Edit/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Replay all' })).toBeDisabled()
  })

  it('hands a rule’s waiting messages to this shell’s Bulk replay, whose preview comes first', async () => {
    const user = userEvent.setup()
    vi.mocked(rulesApi.fetchRuleMatches).mockResolvedValue([5, 6])
    vi.mocked(bulkApi.previewBulk).mockRejectedValue(new Error('not needed here'))
    const { router } = open('/next/azure/rules')
    await user.click(await screen.findByRole('button', { name: 'Manage Order lock losses' }))
    const button = await screen.findByRole('button', { name: 'Replay all' })
    await waitFor(() => expect(button).toBeEnabled())
    await user.click(button)
    await waitFor(() => expect(router.state.location.search).toContain('dialog=bulk-replay'))
    expect(bulkSelection.get()).toEqual({ ids: [5, 6] })
    expect(bulkApi.startBulk).not.toHaveBeenCalled()
  })

  it('sends a held rule’s person to this shell’s approvals, not the old Home tab', async () => {
    const user = userEvent.setup()
    vi.mocked(rulesApi.fetchRules).mockResolvedValue([rule({ askedCount: 3, lastAskedReason: 'AUTONOMY_GRANT_INSUFFICIENT' })])
    open('/next/azure/rules')
    await user.click(await screen.findByRole('button', { name: 'Manage Order lock losses' }))
    expect(await screen.findByRole('link', { name: /See them/ })).toHaveAttribute('href', '/next/azure/approvals')
  })

  it('turns an Operator’s switch-on into a refusal that says who can grant it', async () => {
    const user = userEvent.setup()
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me({ governanceActive: true, effectiveRole: 'Operator', recoverRole: 'Operator' }))
    vi.mocked(rulesApi.fetchRules).mockResolvedValue([off])
    open('/next/azure/rules')
    await user.click(await screen.findByRole('button', { name: 'Manage Image retries' }))
    expect(await screen.findByRole('switch', { name: 'Image retries is off' })).toBeDisabled()
    expect(screen.getByRole('button', { name: /^New rule$|^\+?\s*New rule$/ })).toBeInTheDocument()
    expect(screen.getAllByText(/you need the Approver role/).length).toBeGreaterThan(0)
  })

  it('opens the new-rule form from the page, and from a failure’s link (?rule=)', async () => {
    const user = userEvent.setup()
    open('/next/azure/rules')
    await user.click(await screen.findByRole('button', { name: /^New rule$|^\+?\s*New rule$/ }))
    expect(await screen.findByRole('form', { name: 'New rule' })).toBeInTheDocument()
    open('/next/azure/rules?rule=h1')
    expect((await screen.findAllByRole('form', { name: 'New rule' })).length).toBeGreaterThan(0)
  })

  it('makes rules only after a confirmation, and says when there was nothing to make', async () => {
    const user = userEvent.setup()
    open('/next/azure/rules')
    await user.click(await screen.findByRole('button', { name: /^Auto generate rules$/ }))
    expect(rulesApi.generateRules).not.toHaveBeenCalled()
    await user.click(screen.getByRole('button', { name: 'Yes, make them' }))
    expect(await screen.findByText(/Nothing to make/)).toBeInTheDocument()
    expect(rulesApi.generateRules).toHaveBeenCalledWith('azure')
  })

  it('shows the verification note only where a connected cloud cannot verify a fix', async () => {
    open('/next/azure/rules')
    await screen.findByRole('table', { name: 'Auto Replay rules' })
    expect(screen.queryByText(/can only prove itself/)).not.toBeInTheDocument()
    vi.mocked(namespacesApi.fetchNamespaces).mockResolvedValue([{ id: 'n', provider: 'azure', capabilities: { canProveDlqAbsence: false } } as unknown as namespacesApi.Namespace])
    open('/next/azure/rules')
    expect((await screen.findAllByText(/can only prove itself/)).length).toBeGreaterThan(0)
  })

  it('says so when there are no rules, and shows an error rather than an empty list when the read fails', async () => {
    vi.mocked(rulesApi.fetchRules).mockResolvedValueOnce([])
    open('/next/azure/rules')
    expect(await screen.findByText(/No rules yet/)).toBeInTheDocument()
    vi.mocked(rulesApi.fetchRules).mockRejectedValue(new Error('boom'))
    open('/next/aws/rules')
    expect(await screen.findByRole('alert')).toHaveTextContent('could not be read')
  })

  it('has no accessibility violations', async () => {
    const { container } = open('/next/azure/rules')
    await screen.findByRole('table', { name: 'Auto Replay rules' })
    await expectNoAxeViolations(container)
  })
})
