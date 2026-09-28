import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as rulesApi from '../../lib/api/rules'
import * as replayApi from '../../lib/api/replay'
import * as identity from '../../lib/api/identity'
import { bulkSelection } from '../../lib/bulkSelection'
import { AutoReplayPage } from './AutoReplayPage'

vi.mock('../../lib/api/rules')
vi.mock('../../lib/api/replay')
vi.mock('../../lib/api/identity', async (original) => ({ ...(await original<typeof identity>()), fetchMe: vi.fn() }))

const rule = (over: Partial<rulesApi.Rule> = {}): rulesApi.Rule => ({
  id: 1, name: 'Payment timeouts', provider: 'azure', reason: 'Timeout', entityName: 'payments-dlq', signatureHash: 'h', maxPerHour: 10, waitSeconds: 120, backOff: true,
  enabled: true, disabledReason: null, disabledDetail: null, updatedAt: null, askedCount: 0, lastAskedReason: null, replayed: 0, lastReplayedAt: null,
  verifiedOutcomes: 0, stayedFixed: 0, sampleSize: 20, successFloor: 0.5, ...over,
})

function renderPage() {
  return render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter><AutoReplayPage provider="azure" onClose={() => {}} /></MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('Auto Replay page', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(rulesApi.fetchRuleSources).mockResolvedValue([])
    vi.mocked(identity.fetchMe).mockResolvedValue({ ownerId: 'o', authMethod: 'session', actor: { identity: 's', kind: 'user', label: 'l', isSession: true }, effectiveRole: 'Admin', governanceActive: false } as identity.Me)
  })

  it('a Viewer is told in words, not only a tooltip, what is missing and who can grant it (6.1)', async () => {
    vi.mocked(identity.fetchMe).mockResolvedValue({ ownerId: 'o', authMethod: 'session', actor: { identity: 's', kind: 'user', label: 'l', isSession: true }, effectiveRole: 'Viewer', recoverRole: 'Viewer', governanceActive: true, grantors: ['Ada'] } as identity.Me)
    vi.mocked(rulesApi.fetchRules).mockResolvedValue([rule()])
    renderPage()
    expect((await screen.findAllByText(/you need the Operator role/)).length).toBeGreaterThan(0)
    expect(screen.getAllByText(/Ada can grant it/).length).toBeGreaterThan(0)
  })

  it('a failed read keeps the page title and a way to close it (6.1)', async () => {
    vi.mocked(rulesApi.fetchRules).mockRejectedValue(new Error('down'))
    renderPage()
    expect(await screen.findByText(/couldn't read the rules just now/)).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /Auto Replay/ })).toBeInTheDocument()
    expect(screen.getByLabelText('Close')).toBeInTheDocument()
  })

  it('opens the explainer when there are no rules and offers a first rule', async () => {
    vi.mocked(rulesApi.fetchRules).mockResolvedValue([])
    renderPage()

    expect(await screen.findByRole('button', { name: /Create your first rule/ })).toBeInTheDocument()
    expect(screen.getByText(/You name a failure/)).toBeVisible()
  })

  it('totals what rules really did, and tests a rule without sending anything', async () => {
    vi.mocked(rulesApi.fetchRules).mockResolvedValue([rule({ replayed: 3, askedCount: 2 }), rule({ id: 2, name: 'Other', replayed: 4 })])
    vi.mocked(rulesApi.testRule).mockResolvedValue({ days: 7, matched: 5, stillWaiting: 0, wouldRun: 0, heldBack: 0, holds: [] })
    renderPage()

    expect(await screen.findByText('7')).toBeInTheDocument() // 3 + 4 replayed
    await userEvent.click((await screen.findAllByRole('button', { name: 'Test this rule' }))[0])
    expect(await screen.findByText(/it would have matched/)).toBeInTheDocument()
    expect(rulesApi.testRule).toHaveBeenCalledWith({ provider: 'azure', reason: 'Timeout', entityName: 'payments-dlq', signatureHash: 'h' })
  })

  it('shows the replays a rule sent, from its history', async () => {
    vi.mocked(rulesApi.fetchRules).mockResolvedValue([rule({ replayed: 1 })])
    vi.mocked(replayApi.fetchReplays).mockResolvedValue({
      total: 1, page: 1, pageSize: 5,
      items: [{ id: 9, dlqMessageId: 42, sourceEntity: 'payments-dlq', targetEntity: 'payments', replayedAt: '2026-09-27T10:00:00Z', outcomeStatus: 'accepted', verification: { status: 'verified' } }] as never,
    })
    renderPage()

    await userEvent.click(await screen.findByText('What it replayed'))
    expect(await screen.findByText('stayed fixed')).toBeInTheDocument()
    // The row opens the message's details in a modal over this page.
    expect(screen.getByRole('link', { name: 'payments-dlq → payments' })).toHaveAttribute('href', expect.stringContaining('view=modal'))
    expect(replayApi.fetchReplays).toHaveBeenCalledWith({ provider: 'azure', ruleId: 1, pageSize: 5 })
  })

  it('deletes a rule only after asking, and says its history stays', async () => {
    vi.mocked(rulesApi.fetchRules).mockResolvedValue([rule()])
    vi.mocked(rulesApi.deleteRule).mockResolvedValue()
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Delete Payment timeouts' }))
    expect(rulesApi.deleteRule).not.toHaveBeenCalled()
    expect(screen.getByText(/stays in Replayed/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Yes, delete it' }))
    expect(rulesApi.deleteRule).toHaveBeenCalledWith(1)
  })

  it('edits a rule\'s name and pace, never what it matches', async () => {
    vi.mocked(rulesApi.fetchRules).mockResolvedValue([rule()])
    vi.mocked(rulesApi.updateRule).mockResolvedValue(rule())
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Edit' }))
    const form = screen.getByRole('form', { name: 'Edit Payment timeouts' })
    const name = within(form).getByLabelText('Name')
    await userEvent.clear(name)
    await userEvent.type(name, 'Renamed')
    await userEvent.click(within(form).getByRole('button', { name: 'Save' }))

    expect(rulesApi.updateRule).toHaveBeenCalledWith(1, { name: 'Renamed', maxPerHour: 10, waitSeconds: 120, backOff: true })
  })

  it('hands the rule\'s waiting messages to Bulk Replay instead of sending anything itself', async () => {
    vi.mocked(rulesApi.fetchRules).mockResolvedValue([rule()])
    vi.mocked(rulesApi.fetchRuleMatches).mockResolvedValue([11, 12])
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: /Replay all/ }))

    await vi.waitFor(() => expect(bulkSelection.get()).toEqual({ ids: [11, 12] }))
  })

  it('makes rules only after confirming', async () => {
    vi.mocked(rulesApi.fetchRules).mockResolvedValue([])
    vi.mocked(rulesApi.generateRules).mockResolvedValue([rule()])
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: /Auto Generate Rules/ }))
    expect(rulesApi.generateRules).not.toHaveBeenCalled()
    await userEvent.click(screen.getByRole('button', { name: 'Yes, make them' }))
    expect(rulesApi.generateRules).toHaveBeenCalledWith('azure')
    expect(await screen.findByText(/Made 1 rule/)).toBeInTheDocument()
  })

  it('opens the create form from a prefill link once, and closing it drops the link\'s parameter', async () => {
    vi.mocked(rulesApi.fetchRules).mockResolvedValue([rule()])
    vi.mocked(rulesApi.fetchRuleSources).mockResolvedValue([{ signatureHash: 'h', reason: 'Timeout', entityName: 'payments-dlq', messages: 3, exampleError: null }])
    vi.mocked(rulesApi.testRule).mockResolvedValue({ days: 7, matched: 0, stillWaiting: 0, wouldRun: 0, heldBack: 0, holds: [] })
    render(
      <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
        <MemoryRouter initialEntries={['/?panel=rules&rule=h']}><AutoReplayPage provider="azure" onClose={() => {}} /></MemoryRouter>
      </QueryClientProvider>,
    )

    expect(await screen.findByRole('form', { name: 'New rule' })).toBeInTheDocument()
    expect(await screen.findByText(/A rule for this failure already exists/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(screen.queryByRole('form', { name: 'New rule' })).not.toBeInTheDocument()
  })
})
