import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as agentsApi from '@/lib/api/agents'
import * as componentsApi from '@/lib/api/components'
import * as coverageApi from '@/lib/api/coverage'
import * as identityApi from '@/lib/api/identity'
import * as settingsApi from '@/lib/api/settings'
import { nextRoute } from '@/next/router'
import { agent, component, coverageRows, me } from './homeData'

vi.mock('@/lib/api/coverage', async (o) => ({ ...(await o<typeof coverageApi>()), fetchCoverage: vi.fn() }))
vi.mock('@/lib/api/components', async (o) => ({ ...(await o<typeof componentsApi>()), fetchComponents: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn() }))

const off = { active: false, by: null, at: null, reason: null }

async function open(url = '/next/coverage') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const view = render(<QueryClientProvider client={client}><RouterProvider router={createMemoryRouter([nextRoute], { initialEntries: [url] })} /></QueryClientProvider>)
  // The shell is a lazy chunk (initial-bundle budget), so wait for it before anything asks for the header or the menu.
  await screen.findByRole('banner')
  return view
}

const header = () => screen.getByRole('banner')

describe('the 5.0.0 header (unit B3)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [component()], generatedAtUtc: '2026-10-18T10:00:00Z' })
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me({ recoverRole: 'Approver' }))
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue(off)
  })

  it.each([['Viewer'], ['Operator'], ['Approver'], ['Admin']] as const)('shows the %s role from /me', async (role) => {
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me({ recoverRole: role }))
    await open()
    expect(await within(header()).findByText(`Role: ${role}`)).toBeInTheDocument()
  })

  it('falls back to the fleet role when there is no recovery role, and says "No role" when none is granted', async () => {
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me({ recoverRole: undefined, effectiveRole: 'Operator' }))
    const { unmount } = await open()
    expect(await within(header()).findByText('Role: Operator')).toBeInTheDocument()
    unmount()
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me({ recoverRole: null, effectiveRole: null }))
    await open()
    expect(await within(header()).findByText('Role: No role')).toBeInTheDocument()
  })

  it('says the role is unknown when /me cannot be read, rather than guessing one', async () => {
    vi.mocked(identityApi.fetchMe).mockRejectedValue(new Error('down'))
    await open()
    expect(await within(header()).findByText('Role: unknown')).toBeInTheDocument()
  })

  it('shows the Agent running, and paused when every acting agent is paused', async () => {
    const { unmount } = await open()
    expect(await within(header()).findByText('Agent running')).toBeInTheDocument()
    unmount()
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent({ isPaused: true })])
    await open()
    expect(await within(header()).findByText('Agent paused')).toBeInTheDocument()
    expect(within(header()).getByText('Will not act until resumed')).toBeInTheDocument()
  })

  it('links the Emergency stop button to its dialog and shows no band while the stop is off', async () => {
    await open('/next/coverage')
    const button = await within(header()).findByRole('link', { name: 'Emergency stop' })
    expect(button).toHaveAttribute('href', '/next/coverage?dialog=emergency-stop')
    expect(await screen.findByText('Role: Approver')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('shows a band above the page and says so in the header while the stop is on, with who set it and why', async () => {
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: true, by: 'maya', at: '2026-10-18T09:00:00Z', reason: 'drill' })
    await open()
    const band = await screen.findByRole('alert')
    expect(band).toHaveTextContent('Emergency stop is ON')
    expect(band).toHaveTextContent('Set by maya')
    expect(band).toHaveTextContent('Reason: drill')
    expect(within(header()).getByRole('link', { name: 'Emergency stop is ON' })).toBeInTheDocument()
  })

  it('treats a stop it cannot read as ON', async () => {
    vi.mocked(settingsApi.fetchEmergencyStop).mockRejectedValue(new Error('down'))
    await open()
    const band = await screen.findByRole('alert')
    expect(band).toHaveTextContent('cannot be read, so it is shown as ON')
    expect(within(header()).getByRole('link', { name: 'Emergency stop is ON' })).toBeInTheDocument()
  })

  it('puts the same sums the tiles show on the two tool links, and draws no badge for zero', async () => {
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({
      components: [component({ needsYou: 3, approvals: 2, handOffs: 1 }), component({ componentKey: 'storage-queues', needsYou: 0, approvals: 0, handOffs: 0 })],
      generatedAtUtc: '2026-10-18T10:00:00Z',
    })
    await open('/next/azure')
    const menu = screen.getByRole('navigation', { name: 'ServiceHub' })
    expect(await within(menu).findByLabelText('3 need your attention')).toBeInTheDocument()
    expect(within(menu).getByLabelText('2 pending approvals')).toBeInTheDocument()
  })

  it('colours the header by the cloud being looked at', async () => {
    await open('/next/aws')
    await screen.findByText('Role: Approver')
    expect(header().className).toContain('bg-[#232f3e]')
  })
})
