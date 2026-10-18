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
import { agent, component, coverageRows, me, page } from './homeData'

vi.mock('@/lib/api/coverage', async (o) => ({ ...(await o<typeof coverageApi>()), fetchCoverage: vi.fn() }))
vi.mock('@/lib/api/components', async (o) => ({ ...(await o<typeof componentsApi>()), fetchComponents: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/pendingWork', async (o) => ({ ...(await o<typeof pendingApi>()), fetchPendingWork: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn(), setEmergencyStop: vi.fn() }))

const at = '2026-10-18T10:00:00Z'
const off = { active: false, by: null, at: null, reason: null }
const sent = () => vi.mocked(settingsApi.setEmergencyStop).mock.calls.map((c) => c[0])
const open = (u = '/next/azure?dialog=emergency-stop') => {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter([nextRoute], { initialEntries: [u] })
  return { router, ...render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>) }
}

describe('Emergency stop dialog (unit F5)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [component()], generatedAtUtc: at })
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([]))
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue(off)
    vi.mocked(settingsApi.setEmergencyStop).mockResolvedValue({ active: true, by: 'maya', at, reason: 'outage' })
  })

  it('opens from the header link, says what stops and what does NOT, and sends nothing on open', async () => {
    const { router } = open('/next/azure')
    await userEvent.setup().click(await screen.findByRole('link', { name: /Emergency stop/ }))
    const dialog = await screen.findByRole('dialog')
    expect(router.state.location.search).toBe('?dialog=emergency-stop')
    expect(await within(dialog).findByText(/stops every replay ServiceHub would do on its own/)).toBeInTheDocument()
    expect(within(dialog).getByText('A person can still replay one message')).toBeInTheDocument()
    expect(within(dialog).getByText(/A bulk replay a person already started may carry on/)).toBeInTheDocument()
    expect(settingsApi.setEmergencyStop).not.toHaveBeenCalled()
  })

  it('is never one click: a reason AND the typed word STOP are both required', async () => {
    const user = userEvent.setup()
    open()
    const go = await screen.findByRole('button', { name: 'Stop all automatic replays' })
    expect(go).toBeDisabled()
    await user.type(screen.getByLabelText(/^Why/), 'Payment gateway outage')
    expect(go).toBeDisabled()
    await user.type(screen.getByLabelText('Type STOP to confirm'), 'stop')
    expect(go).toBeDisabled() // the word is exact
    await user.clear(screen.getByLabelText('Type STOP to confirm'))
    await user.type(screen.getByLabelText('Type STOP to confirm'), 'STOP')
    expect(go).toBeEnabled()
    await user.click(go)
    await waitFor(() => expect(sent()).toEqual([{ active: true, reason: 'Payment gateway outage', confirm: 'STOP' }]))
  })

  it('when it is on, offers only to switch it off, behind a reason and the word LIFT', async () => {
    const user = userEvent.setup()
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: true, by: 'maya', at, reason: 'outage' })
    vi.mocked(settingsApi.setEmergencyStop).mockResolvedValue(off)
    open()
    expect(await screen.findByText(/Emergency stop is on/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Stop all automatic replays' })).not.toBeInTheDocument()
    await user.type(screen.getByLabelText(/Why is it safe to resume/), 'gateway healthy')
    await user.type(screen.getByLabelText('Type LIFT to confirm'), 'LIFT')
    await user.click(screen.getByRole('button', { name: 'Switch emergency stop off' }))
    await waitFor(() => expect(sent()).toEqual([{ active: false, reason: 'gateway healthy', confirm: 'LIFT' }]))
  })

  it('refuses someone who is not an Admin, saying who can grant it', async () => {
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me({ governanceActive: true, effectiveRole: 'Approver', recoverRole: 'Approver' }))
    const user = userEvent.setup()
    open()
    await user.type(await screen.findByLabelText(/^Why/), 'x')
    await user.type(screen.getByLabelText('Type STOP to confirm'), 'STOP')
    await waitFor(() => expect(screen.getByRole('button', { name: 'Stop all automatic replays' })).toBeDisabled())
    expect(screen.getAllByText(/you need the Admin role/).length).toBeGreaterThan(0)
  })

  it('says it cannot read the stop, and so treats it as on, rather than offering a switch', async () => {
    vi.mocked(settingsApi.fetchEmergencyStop).mockRejectedValue(new Error('boom'))
    open()
    expect(await screen.findByRole('alert')).toHaveTextContent('behaves as if it is on')
    expect(screen.queryByRole('button', { name: /Stop all automatic replays|Switch emergency stop off/ })).not.toBeInTheDocument()
  })

  it('shows the server’s refusal and changes nothing when the call fails', async () => {
    const user = userEvent.setup()
    vi.mocked(settingsApi.setEmergencyStop).mockRejectedValue(new Error('server said no'))
    open()
    await user.type(await screen.findByLabelText(/^Why/), 'x')
    await user.type(screen.getByLabelText('Type STOP to confirm'), 'STOP')
    await user.click(screen.getByRole('button', { name: 'Stop all automatic replays' }))
    expect((await screen.findAllByRole('alert')).map((a) => a.textContent).join(' ')).toContain('Nothing was changed')
  })

  it('has no accessibility violations', async () => {
    open()
    const dialog = await screen.findByRole('dialog')
    await within(dialog).findByLabelText(/^Why/)
    await expectNoAxeViolations(dialog)
  })
})
