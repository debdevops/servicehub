import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as rulesApi from '../../lib/api/rules'
import { ProviderScopeContext } from '../provider/providerScope'
import AutoReplayPanel from './AutoReplayPanel'

vi.mock('../../lib/api/rules')

const rule = (over: Partial<rulesApi.Rule> = {}): rulesApi.Rule => ({
  id: 1, name: 'Payment timeouts', provider: 'azure', reason: 'Timeout', entityName: 'payments-dlq', signatureHash: 'h', maxPerHour: 10, waitSeconds: 120, backOff: true,
  enabled: true, disabledReason: null, disabledDetail: null, updatedAt: null, askedCount: 0, lastAskedReason: null, replayed: 0, lastReplayedAt: null,
  verifiedOutcomes: 0, stayedFixed: 0, sampleSize: 20, successFloor: 0.5, ...over,
})

function renderPanel(initial = '/?panel=rules') {
  return render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <ProviderScopeContext.Provider value={{ selected: 'azure', select: () => {} }}>
        <MemoryRouter initialEntries={[initial]}><AutoReplayPanel entry={{} as never} close={() => {}} /></MemoryRouter>
      </ProviderScopeContext.Provider>
    </QueryClientProvider>,
  )
}

describe('Auto Replay panel', () => {
  beforeEach(() => { vi.clearAllMocks(); vi.mocked(rulesApi.fetchRuleSources).mockResolvedValue([]) })

  it('lists rules with their real outcomes, and says a rule with none has none', async () => {
    vi.mocked(rulesApi.fetchRules).mockResolvedValue([rule({ verifiedOutcomes: 20, stayedFixed: 18 }), rule({ id: 2, name: 'Fresh rule' })])
    renderPanel()

    expect(await screen.findByText('18 of 20 stayed fixed')).toBeInTheDocument()
    expect(screen.getByText('no verified outcomes yet')).toBeInTheDocument()
    expect(screen.getByText(/2 rules, 2 on/)).toBeInTheDocument()
  })

  it('says matching messages are waiting for a person, and why, rather than implying it is replaying', async () => {
    vi.mocked(rulesApi.fetchRules).mockResolvedValue([rule({ askedCount: 4, lastAskedReason: 'AUTONOMY_GRANT_INSUFFICIENT' })])
    renderPanel()

    expect(await screen.findByText(/4 matching messages are waiting for a person/)).toBeInTheDocument()
    expect(screen.getByText(/hasn’t earned the right to replay this failure on its own yet/)).toBeInTheDocument()
  })

  it('shows a tripped breaker with its reason, cannot be switched on by the toggle, and asks twice to turn back on', async () => {
    vi.mocked(rulesApi.fetchRules).mockResolvedValue([rule({ enabled: false, disabledReason: 'CircuitBreaker', disabledDetail: 'Only 9 of its last 20 replays stayed fixed (45%) — below the 50% floor.' })])
    vi.mocked(rulesApi.setRuleEnabled).mockResolvedValue(rule())
    renderPanel()

    expect(await screen.findByText('Stopped itself')).toBeInTheDocument()
    expect(screen.getByText(/Only 9 of its last 20 replays stayed fixed/)).toBeInTheDocument()
    expect(screen.getByRole('switch')).toBeDisabled()

    await userEvent.click(screen.getByRole('button', { name: 'Turn back on…' }))
    expect(rulesApi.setRuleEnabled).not.toHaveBeenCalled()
    await userEvent.click(screen.getByRole('button', { name: 'Yes, turn it back on' }))
    expect(rulesApi.setRuleEnabled).toHaveBeenCalledWith(1, true)
  })

  it('creates a rule from a failure already seen and tests it before it is made', async () => {
    vi.mocked(rulesApi.fetchRules).mockResolvedValue([])
    vi.mocked(rulesApi.fetchRuleSources).mockResolvedValue([{ signatureHash: 'h1', reason: 'Timeout', entityName: 'inventory-dlq', messages: 5, exampleError: null }])
    vi.mocked(rulesApi.testRule).mockResolvedValue({ days: 7, matched: 5, stillWaiting: 5, wouldRun: 4, heldBack: 1, holds: [{ reasonCode: 'RECURRENCE_CAP_EXCEEDED', remedy: '', count: 1 }] })
    vi.mocked(rulesApi.createRule).mockResolvedValue(rule())
    renderPanel()

    await userEvent.click(await screen.findByRole('button', { name: /New rule/ }))
    await userEvent.selectOptions(await screen.findByLabelText('Based on'), 'h1')

    const form = screen.getByRole('form', { name: 'New rule' })
    expect(await within(form).findByText(/would have matched/)).toHaveTextContent('5 messages')
    await userEvent.click(within(form).getByRole('button', { name: 'Create and turn on' }))
    expect(rulesApi.createRule).toHaveBeenCalledWith(expect.objectContaining({ provider: 'azure', reason: 'Timeout', entityName: 'inventory-dlq', signatureHash: 'h1', waitSeconds: 120 }))
  })

  it('opens the form with the failure already chosen when a signature page linked here', async () => {
    vi.mocked(rulesApi.fetchRules).mockResolvedValue([])
    vi.mocked(rulesApi.fetchRuleSources).mockResolvedValue([{ signatureHash: 'h1', reason: 'Timeout', entityName: 'inventory-dlq', messages: 5, exampleError: null }])
    vi.mocked(rulesApi.testRule).mockResolvedValue({ days: 7, matched: 0, stillWaiting: 0, wouldRun: 0, heldBack: 0, holds: [] })
    renderPanel('/?panel=rules&rule=h1')

    expect(await screen.findByDisplayValue('Timeout in inventory-dlq')).toBeInTheDocument()
  })
})
