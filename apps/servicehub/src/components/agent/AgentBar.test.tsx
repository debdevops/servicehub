import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as api from '../../lib/api/agents'
import * as recovery from '../../lib/api/recovery'
import * as pending from '../../lib/api/pendingWork'
import type { Agent } from '../../lib/api/agents'
import type { Namespace } from '../../lib/api/namespaces'
import { AgentBar } from './AgentBar'

vi.mock('../../lib/api/agents', async (original) => ({ ...(await original<typeof api>()), fetchAgents: vi.fn(), pauseAgent: vi.fn(), resumeAgent: vi.fn() }))
vi.mock('../../lib/api/recovery', async (original) => ({ ...(await original<typeof recovery>()), fetchRecoverySummary: vi.fn() }))
vi.mock('../../lib/api/pendingWork', async (original) => ({ ...(await original<typeof pending>()), fetchPendingWork: vi.fn() }))

const a = (id: string, canAct: boolean, isPaused = false) => ({ id, name: id, canAct, isPaused }) as unknown as Agent
const azure = { id: 'n1', provider: 'azure', capabilities: { canProveDlqAbsence: true, supportsRepeatablePeek: true } } as unknown as Namespace

function renderBar() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}><MemoryRouter><AgentBar cloud="Azure" provider="azure" namespaces={[azure]} queues={12} /></MemoryRouter></QueryClientProvider>)
}

describe('the agent bar on Home', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(recovery.fetchRecoverySummary).mockResolvedValue({
      window: '24h', total: 25, states: [{ state: 'Recovered', count: 18 }, { state: 'Observing', count: 5 }, { state: 'Returned', count: 1 }],
      byProvider: [], stayedFixedRate: 18 / 19, returnedConfidence: { exact: 1, heuristic: 0 }, replaysAccepted: 24,
    } as never)
    vi.mocked(pending.fetchPendingWork).mockResolvedValue({ items: [], total: 2, byProvider: [], agents: 0 })
  })

  it('shows its four numbers: three from the recovery summary and "need you" from the same pending-work query as the bell', async () => {
    vi.mocked(api.fetchAgents).mockResolvedValue([a('auto-replay', true), a('dlq-monitor', false)])
    renderBar()
    expect(await screen.findByText('18')).toBeInTheDocument()
    expect(await screen.findByText('95%')).toBeInTheDocument()
    expect(screen.getByText('being watched now')).toBeInTheDocument()
    expect(await screen.findByText('2')).toBeInTheDocument()
    expect(screen.getByText('need you — above')).toBeInTheDocument()
    expect(screen.getByText(/Watching 12 queues/)).toBeInTheDocument()
  })

  it('pause pauses only the acting agents, so "will not act — still watching" stays true', async () => {
    vi.mocked(api.fetchAgents).mockResolvedValue([a('auto-replay', true), a('bulk-replay', true), a('dlq-monitor', false)])
    renderBar()
    await userEvent.click(await screen.findByRole('button', { name: /Pause/ }))
    expect(api.pauseAgent).toHaveBeenCalledTimes(2)
    expect(api.pauseAgent).toHaveBeenCalledWith('auto-replay')
    expect(api.pauseAgent).toHaveBeenCalledWith('bulk-replay')
  })

  it('offers resume for every paused agent, including one paused on the Agents page', async () => {
    vi.mocked(api.fetchAgents).mockResolvedValue([a('auto-replay', true, true), a('bulk-replay', true, true), a('dlq-monitor', false, true)])
    renderBar()
    expect(await screen.findByText('WILL NOT ACT')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: /Resume all 3/ }))
    expect(api.resumeAgent).toHaveBeenCalledTimes(3)
  })
})
