import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as api from '../../lib/api/agents'
import type { Agent } from '../../lib/api/agents'
import AgentsPage, { fold } from './AgentsPage'
import { expectNoAxeViolations } from '../../test/axe'

vi.mock('../../lib/api/agents', async (original) => ({
  ...(await original<typeof api>()),
  fetchAgents: vi.fn(), fetchAgentActivity: vi.fn(), pauseAgent: vi.fn(), resumeAgent: vi.fn(),
}))

const agent = (over: Partial<Agent>): Agent => ({
  id: 'x', name: 'X', purpose: 'Does x.', kind: 'watch', authority: 'observes', canAct: false, cadenceSeconds: 30, notes: null,
  may: [], mayNot: ['Change anything'], health: 'healthy', late: false, isPaused: false, lastRunUtc: new Date().toISOString(),
  lastResult: { examined: 3, changed: 0, summary: 'looked at 3 queues', degraded: false }, lastFailure: null, consecutiveFailures: 0, ...over,
})

const agents = [
  agent({ id: 'dlq-monitor', name: 'Dead-letter Monitor', purpose: 'Looks in every queue.' }),
  agent({ id: 'auto-replay', name: 'Auto Replay', kind: 'act', authority: 'actsAutonomously', canAct: true, may: ['Replay matching messages'], mayNot: ['Replay in Production'] }),
  agent({ id: 'throwaway', name: 'Throwaway', kind: 'maintain', purpose: 'Proves a new agent needs no UI.' }),
]

function renderPage(url = '/advanced/agents') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}><MemoryRouter initialEntries={[url]}><AgentsPage /></MemoryRouter></QueryClientProvider>)
}

describe('the Agents page', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(api.fetchAgents).mockResolvedValue(agents)
    vi.mocked(api.fetchAgentActivity).mockResolvedValue({ agentId: 'auto-replay', cyclesSinceUtc: null, items: [] })
    vi.mocked(api.pauseAgent).mockResolvedValue(agents[1])
  })

  it('has no accessibility violations (6.6)', async () => {
    renderPage()
    await new Promise((r) => setTimeout(r, 150))
    await expectNoAxeViolations(document.body)
  })

  it('leads with the agents that can change anything, and draws any agent it is given with no code of its own', async () => {
    renderPage()
    const acting = await screen.findByRole('region', { name: /ACTING \(1\)/ })
    expect(within(acting).getByText('Auto Replay')).toBeInTheDocument()
    const watching = screen.getByRole('region', { name: /WATCHING \(2\)/ })
    expect(within(watching).getByText('Throwaway')).toBeInTheDocument()
    expect(within(watching).getByText('Proves a new agent needs no UI.')).toBeInTheDocument()
    expect(screen.getByText('3 agents')).toBeInTheDocument()
  })

  it('opens the acting agent first and shows what it may and may never do', async () => {
    renderPage()
    const detail = await screen.findByRole('complementary', { name: 'Auto Replay details' })
    expect(within(detail).getByText('Replay matching messages')).toBeInTheDocument()
    expect(within(detail).getByText('Replay in Production')).toBeInTheDocument()
    expect(within(detail).getByText(/will/)).toHaveTextContent(/not act/)
  })

  it('pauses, and never offers resume — Advanced can only take authority away', async () => {
    vi.mocked(api.fetchAgents).mockResolvedValue([agents[0], { ...agents[1], isPaused: true, health: 'paused' }])
    renderPage()
    const detail = await screen.findByRole('complementary', { name: 'Auto Replay details' })
    expect(within(detail).getByText(/Resume it from Home/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /resume/i })).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Pause Dead-letter Monitor' }))
    expect(api.pauseAgent).toHaveBeenCalledWith('dlq-monitor')
  })
})

describe('fold', () => {
  it('folds identical repeated cycles into one line marked with how many', () => {
    const bad = { at: 't', source: 'cycle' as const, kind: 'degraded', text: '1 cloud could not be read', by: null }
    expect(fold([bad, bad, bad]).map((i) => i.text)).toEqual(['1 cloud could not be read ×3'])
  })

  it('turns a run of idle cycles into one line that counts them', () => {
    const idle = { at: 't', source: 'cycle' as const, kind: 'idle', text: 'examined 0 · nothing to do', by: null }
    const acted = { ...idle, kind: 'acted', text: 'replayed 2' }
    expect(fold([idle, idle, idle, acted, idle]).map((i) => i.text)).toEqual(['3 cycles with nothing to do', 'replayed 2', 'examined 0 · nothing to do'])
  })
})
