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
import { nextRoute } from '@/next/router'
import { expectNoAxeViolations } from '@tests/support/axe'
import { agent, approval, component, coverageRows, me, page } from './homeData'

vi.mock('@/lib/api/coverage', async (o) => ({ ...(await o<typeof coverageApi>()), fetchCoverage: vi.fn() }))
vi.mock('@/lib/api/components', async (o) => ({ ...(await o<typeof componentsApi>()), fetchComponents: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn() }))
vi.mock('@/lib/api/pendingWork', async (o) => ({ ...(await o<typeof pendingApi>()), fetchPendingWork: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({
  ...(await o<typeof agentsApi>()), fetchAgents: vi.fn(), fetchDormantAgents: vi.fn(), fetchAgentActivity: vi.fn(), pauseAgent: vi.fn(), resumeAgent: vi.fn(),
}))

const at = '2026-10-18T10:00:00Z'
const acting = (over: Partial<agentsApi.Agent> = {}) => agent({
  id: 'auto-replay', name: 'Auto Replay', purpose: 'Replays what a rule allows.', kind: 'act', authority: 'actsAutonomously', canAct: true, health: 'healthy', lastRunUtc: at,
  lastResult: { examined: 3, changed: 1, summary: 'looked at 3 messages', degraded: false }, ...over,
})
const watching = (over: Partial<agentsApi.Agent> = {}) => agent({
  id: 'dlq-monitor', name: 'Dead-letter monitor', purpose: 'Reads dead-letter queues.', kind: 'watch', authority: 'observes', canAct: false, health: 'healthy', lastRunUtc: at,
  lastResult: { examined: 5, changed: 0, summary: 'looked at 5 queues', degraded: false }, ...over,
})

function open(url: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter([nextRoute], { initialEntries: [url] })
  return { router, ...render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>) }
}

describe('Agents (unit E8)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [component()], generatedAtUtc: at })
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([]))
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([acting(), watching()])
    vi.mocked(agentsApi.fetchDormantAgents).mockResolvedValue([])
    vi.mocked(agentsApi.fetchAgentActivity).mockResolvedValue({ agentId: 'auto-replay', cyclesSinceUtc: at, items: [] })
    vi.mocked(agentsApi.pauseAgent).mockResolvedValue(acting({ isPaused: true }))
  })

  it('lists every registered agent in the descriptor’s own words, with health from its cycles', async () => {
    open('/next/azure/agents')
    await screen.findByRole('heading', { level: 1, name: 'Agents' })
    const table = await screen.findByRole('table', { name: /Agents registered/ })
    const rows = within(table).getAllByRole('row')
    expect(rows[1]).toHaveTextContent('Auto Replay')
    expect(rows[1]).toHaveTextContent('Acts on its own, within limits')
    expect(rows[2]).toHaveTextContent('Observes only')
    expect(rows[2]).toHaveTextContent('looked at 5 queues')
    expect(screen.getByText('Agents', { selector: 'p' }).parentElement).toHaveTextContent('2')
  })

  it('counts failing, degraded and late agents as needing a look, and a paused one as a choice, not a fault', async () => {
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([acting({ health: 'failing', lastFailure: 'cloud unreachable' }), watching({ late: true }), acting({ id: 'bulk', name: 'Bulk replay', isPaused: true, health: 'paused' })])
    open('/next/azure/agents')
    await screen.findByRole('table', { name: /Agents registered/ })
    await waitFor(() => expect(screen.getByText('Need a look', { selector: 'p' }).parentElement).toHaveTextContent('2'))
    expect(screen.getByText('Paused', { selector: 'p' }).parentElement).toHaveTextContent('1')
    expect(screen.getByText('Failed: cloud unreachable')).toBeInTheDocument()
    expect(screen.getByText('Late')).toBeInTheDocument()
  })

  it('pauses exactly one agent, on a click, and offers no Resume for one that is running', async () => {
    const user = userEvent.setup()
    open('/next/azure/agents')
    await user.click(await screen.findByRole('button', { name: 'Pause Auto Replay' }))
    await waitFor(() => expect(agentsApi.pauseAgent).toHaveBeenCalledWith('auto-replay'))
    expect(agentsApi.pauseAgent).toHaveBeenCalledTimes(1)
    expect(agentsApi.resumeAgent).not.toHaveBeenCalled()
    expect(screen.queryByRole('button', { name: /Resume/ })).not.toBeInTheDocument()
  })

  it('disables Pause, with the reason, for someone who may not pause', async () => {
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me({ governanceActive: true, effectiveRole: 'Viewer', recoverRole: 'Viewer' }))
    open('/next/azure/agents')
    const button = await screen.findByRole('button', { name: 'Pause Auto Replay' })
    await waitFor(() => expect(button).toBeDisabled())
    expect(button).toHaveAttribute('title', expect.stringContaining('Operator'))
  })

  it('opens an agent’s detail in the address, with a link to the ledger in this shell', async () => {
    const user = userEvent.setup()
    const { router } = open('/next/azure/agents')
    await user.click(await screen.findByRole('button', { name: 'View Auto Replay' }))
    expect(router.state.location.search).toBe('?agent=auto-replay')
    const detail = await screen.findByRole('complementary', { name: 'Auto Replay details' })
    expect(within(detail).getByRole('link', { name: 'Everything it did, in the Ledger ›' })).toHaveAttribute('href', '/next/azure/ledger')
  })

  describe('Resume lives on this page, for Admins (owner decision 2026-10-19)', () => {
    const paused = () => vi.mocked(agentsApi.fetchAgents).mockResolvedValue([acting({ isPaused: true, health: 'paused' })])
    const resumed = () => vi.mocked(agentsApi.resumeAgent).mock.calls.map((c) => c[0])

    it('resumes exactly one agent, from the row, on a click, and never pauses', async () => {
      const user = userEvent.setup()
      paused()
      vi.mocked(agentsApi.resumeAgent).mockResolvedValue(acting())
      open('/next/azure/agents')
      await user.click(await screen.findByRole('button', { name: 'Resume Auto Replay' }))
      await waitFor(() => expect(resumed()).toEqual(['auto-replay']))
      expect(agentsApi.pauseAgent).not.toHaveBeenCalled()
    })

    it('resumes from the detail too, saying it is recorded with the person’s name', async () => {
      const user = userEvent.setup()
      paused()
      vi.mocked(agentsApi.resumeAgent).mockResolvedValue(acting())
      open('/next/azure/agents?agent=auto-replay')
      const detail = await screen.findByRole('complementary', { name: 'Auto Replay details' })
      expect(within(detail).getByText(/recorded with your name/)).toBeInTheDocument()
      await user.click(within(detail).getByRole('button', { name: 'Resume Auto Replay' }))
      await waitFor(() => expect(resumed()).toEqual(['auto-replay']))
    })

    it('is an Admin’s: an Operator who may pause may not resume, and is told who can grant it', async () => {
      paused()
      vi.mocked(identityApi.fetchMe).mockResolvedValue(me({ governanceActive: true, effectiveRole: 'Operator', recoverRole: 'Operator' }))
      open('/next/azure/agents')
      const button = await screen.findByRole('button', { name: 'Resume Auto Replay' })
      await waitFor(() => expect(button).toBeDisabled())
      expect(button).toHaveAttribute('title', expect.stringContaining('Admin'))
      expect(resumed()).toEqual([])
    })

    it('shows the failure and changes nothing when the server refuses', async () => {
      const user = userEvent.setup()
      paused()
      vi.mocked(agentsApi.resumeAgent).mockRejectedValue(new Error('refused'))
      open('/next/azure/agents')
      await user.click(await screen.findByRole('button', { name: 'Resume Auto Replay' }))
      expect(await screen.findByRole('alert')).toHaveTextContent('Nothing changed')
    })
  })

  it('names an agent that stopped reporting, from the pending-work item it raised', async () => {
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([approval({ id: 'a1', kind: 'agent', agentId: 'auto-replay', reason: 'This agent has stopped reporting.' })]))
    open('/next/azure/agents')
    expect(await screen.findByText('This agent has stopped reporting.')).toBeInTheDocument()
  })

  it('says why an agent is not listed, and shows an error rather than a list when the read fails', async () => {
    vi.mocked(agentsApi.fetchDormantAgents).mockResolvedValue([{ id: 'observer', name: 'Observer', needs: 'ObserverCloud' }])
    open('/next/azure/agents')
    expect(await screen.findByText(/1 agent is off because none of your connected clouds needs it: Observer/)).toBeInTheDocument()
    vi.mocked(agentsApi.fetchAgents).mockRejectedValue(new Error('boom'))
    open('/next/aws/agents')
    expect(await screen.findByRole('alert')).toHaveTextContent('couldn’t read its agents')
  })

  it('has no accessibility violations', async () => {
    const { container } = open('/next/azure/agents')
    await screen.findByRole('table', { name: /Agents registered/ })
    await expectNoAxeViolations(container)
  })
})
