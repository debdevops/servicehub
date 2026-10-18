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
import * as recoveryApi from '@/lib/api/recovery'
import * as settingsApi from '@/lib/api/settings'
import { nextRoute } from '@/next/router'
import { expectNoAxeViolations } from '@tests/support/axe'
import { agent, component, coverageRows, handOff, me, page } from './homeData'

vi.mock('@/lib/api/coverage', async (o) => ({ ...(await o<typeof coverageApi>()), fetchCoverage: vi.fn() }))
vi.mock('@/lib/api/components', async (o) => ({ ...(await o<typeof componentsApi>()), fetchComponents: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn() }))
vi.mock('@/lib/api/pendingWork', async (o) => ({ ...(await o<typeof pendingApi>()), fetchPendingWork: vi.fn(), resolvePending: vi.fn() }))
vi.mock('@/lib/api/recovery', async (o) => ({ ...(await o<typeof recoveryApi>()), fetchLedgerEntry: vi.fn() }))

const at = '2026-10-18T10:00:00Z'
const url = '/next/azure?panel=hand-off&entry=e2'

function open(u = url) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter([nextRoute], { initialEntries: [u] })
  return { router, ...render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>) }
}

describe('Hand-off panel (unit F4)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [component()], generatedAtUtc: at })
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([handOff({ reason: 'ServiceHub stopped before it could record whether it was sent.' })]))
    vi.mocked(pendingApi.resolvePending).mockResolvedValue(undefined)
    vi.mocked(recoveryApi.fetchLedgerEntry).mockResolvedValue({
      entry: {} as never, recoveryMarker: null, markerApplied: false, deadLetterReason: null, verificationResult: null, observationWindowEndsAt: null, lastEventSeq: 2,
      events: [
        { seq: 1, eventType: 'EntryBegun', occurredAt: at, actor: { identity: 'maya', kind: 'user', label: 'Maya (Approver)', isSession: false }, detail: null, prevHash: 'a', entryHash: 'b' },
        { seq: 2, eventType: 'ExecutionUnknown', occurredAt: at, actor: { identity: 'system', kind: 'system', label: 'ServiceHub', isSession: false }, detail: null, prevHash: 'b', entryHash: 'c' },
      ],
    })
  })

  it('says plainly that ServiceHub stopped and a person decides, with why, family and what happened so far', async () => {
    open()
    const panel = await screen.findByRole('dialog')
    expect(await within(panel).findByText(/ServiceHub stopped here. A person decides./)).toBeInTheDocument()
    expect(within(panel).getByText('ServiceHub stopped before it could record whether it was sent.')).toBeInTheDocument()
    expect(within(panel).getAllByText('Outcome unknown').length).toBeGreaterThan(0) // the family label, and the step
    const steps = await within(panel).findByRole('region', { name: 'What happened so far' })
    expect(within(steps).getAllByRole('listitem')[0]).toHaveTextContent('Outcome unknown') // newest first
    expect(pendingApi.resolvePending).not.toHaveBeenCalled()
  })

  it('offers Replay again as a link to the Replay dialog, never a replay itself', async () => {
    open()
    const link = await screen.findByRole('link', { name: 'Replay…' })
    expect(link).toHaveAttribute('href', '/next/azure?dialog=replay&id=42') // the Replay dialog replaces the panel; it has its own preview and checks
  })

  it('settles only with a written reason, recorded in the person’s words', async () => {
    const user = userEvent.setup()
    open()
    await user.click(await screen.findByRole('button', { name: 'Mark as settled…' }))
    const record = screen.getByRole('button', { name: 'Record what I found' })
    expect(record).toBeDisabled()
    await user.type(screen.getByLabelText(/What did you find/), 'the main queue holds one copy')
    await user.click(record)
    await waitFor(() => expect(pendingApi.resolvePending).toHaveBeenCalledWith('e2', 'the main queue holds one copy'))
    expect(await screen.findByText('Recorded with your name.')).toBeInTheDocument()
  })

  it('refuses to settle for someone who is not an Approver, saying who can grant it', async () => {
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me({ governanceActive: true, effectiveRole: 'Operator', recoverRole: 'Operator' }))
    open()
    const button = await screen.findByRole('button', { name: 'Mark as settled…' })
    await waitFor(() => expect(button).toBeDisabled())
    expect(screen.getAllByText(/you need the Approver role/).length).toBeGreaterThan(0)
  })

  it('Leave it closes the panel and changes nothing', async () => {
    const user = userEvent.setup()
    const { router } = open()
    await user.click(await screen.findByRole('button', { name: 'Leave it' }))
    await waitFor(() => expect(router.state.location.search).not.toContain('panel='))
    expect(pendingApi.resolvePending).not.toHaveBeenCalled()
  })

  it('says so when nothing is handed to a person under that link, and when the read fails', async () => {
    open('/next/azure?panel=hand-off&entry=nope')
    expect(await screen.findByText(/Nothing is handed to a person under that link/)).toBeInTheDocument()
    vi.mocked(pendingApi.fetchPendingWork).mockRejectedValue(new Error('boom'))
    open()
    expect((await screen.findAllByRole('alert'))[0]).toHaveTextContent('could not be read')
  })

  it('has no accessibility violations', async () => {
    open()
    const panel = await screen.findByRole('dialog')
    await within(panel).findByText(/ServiceHub stopped here/)
    await expectNoAxeViolations(panel)
  })
})
