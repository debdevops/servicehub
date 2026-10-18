import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as agentsApi from '@/lib/api/agents'
import * as componentsApi from '@/lib/api/components'
import * as coverageApi from '@/lib/api/coverage'
import * as identityApi from '@/lib/api/identity'
import * as pendingApi from '@/lib/api/pendingWork'
import * as settingsApi from '@/lib/api/settings'
import * as signaturesApi from '@/lib/api/signatures'
import { nextRoute } from '@/next/router'
import { expectNoAxeViolations } from '@tests/support/axe'
import { agent, component, coverageRows, me, page } from './homeData'

vi.mock('@/lib/api/coverage', async (o) => ({ ...(await o<typeof coverageApi>()), fetchCoverage: vi.fn() }))
vi.mock('@/lib/api/components', async (o) => ({ ...(await o<typeof componentsApi>()), fetchComponents: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn() }))
vi.mock('@/lib/api/pendingWork', async (o) => ({ ...(await o<typeof pendingApi>()), fetchPendingWork: vi.fn() }))
vi.mock('@/lib/api/signatures', async (o) => ({ ...(await o<typeof signaturesApi>()), fetchAuthority: vi.fn(), fetchSignatures: vi.fn(), fetchSignatureTrust: vi.fn() }))

const at = '2026-10-18T10:00:00Z'
function open(u: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter([nextRoute], { initialEntries: [u] })
  return { router, ...render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>) }
}

beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
  vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [component()], generatedAtUtc: at })
  vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
  vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
  vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
  vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([]))
  vi.mocked(signaturesApi.fetchSignatures).mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 25, all: 0, growing: 0, replayHelps: 0, replayDoesNotHelp: 0 })
  vi.mocked(signaturesApi.fetchAuthority).mockResolvedValue({ total: 3, capped: false, unattended: 0, standing: 1, approve: 2, held: [], needs: { sample: 7, rate: 0.9 } })
})

describe('Security and data (unit F10)', () => {
  it('says what is kept and what ServiceHub may do — and nothing it has not checked', async () => {
    open('/next/azure?panel=security')
    const panel = await screen.findByRole('dialog')
    expect(await within(panel).findByText('Connection strings and keys')).toBeInTheDocument()
    expect(within(panel).getByText('Plain text')).toBeInTheDocument()
    expect(within(panel).getByText(/Not removed by age yet/)).toBeInTheDocument()
    // The two lines the design marks as unchecked (design check C16) are not claimed.
    expect(panel.textContent).not.toMatch(/nothing else leaves/i)
    expect(panel.textContent).not.toMatch(/never reach the logs/i)
    expect(within(panel).getByText(/Not said here yet/)).toBeInTheDocument()
  })

  it('opens from Help', async () => {
    const user = userEvent.setup()
    open('/next/azure?panel=help')
    await user.click(await screen.findByRole('link', { name: 'Security and data →' }))
    expect(await screen.findByText('What ServiceHub keeps on this machine')).toBeInTheDocument()
  })

  it('has no accessibility violations', async () => {
    open('/next/azure?panel=security')
    const panel = await screen.findByRole('dialog')
    await within(panel).findByText('What ServiceHub keeps on this machine')
    await expectNoAxeViolations(panel)
  })
})

describe('How autonomy is earned (unit F11)', () => {
  it('reads the Standing threshold from the server, never from its own copy', async () => {
    open('/next/azure?panel=autonomy')
    const panel = await screen.findByRole('dialog')
    expect(await within(panel).findByText('7 verified fixes at 90% or better, no duplicate')).toBeInTheDocument()
    expect(panel.textContent).not.toMatch(/10 verified fixes at 95%/)
    expect(panel.textContent).not.toMatch(/30 verified fixes at 99%/)
  })

  it('says “Not read”, not a guess, when the thresholds cannot be read', async () => {
    vi.mocked(signaturesApi.fetchAuthority).mockRejectedValue(new Error('boom'))
    open('/next/azure?panel=autonomy')
    const panel = await screen.findByRole('dialog')
    expect(await within(panel).findByText('Not read')).toBeInTheDocument()
  })

  it('says which components can earn it from the coverage catalogue (capability, not a name)', async () => {
    open('/next/azure?panel=autonomy')
    const panel = await screen.findByRole('dialog')
    const table = await within(panel).findByRole('table', { name: 'Which components can earn autonomy' })
    expect(within(table).getByText('Azure Service Bus').closest('tr')).toHaveTextContent('Yes')
    expect(within(table).getByText('AWS SQS').closest('tr')).toHaveTextContent('Not on its own')
  })

  it('does not claim a demotion that is not built', async () => {
    open('/next/azure?panel=autonomy')
    const panel = await screen.findByRole('dialog')
    expect(await within(panel).findByText(/The component’s settings change/)).toBeInTheDocument()
    expect(within(panel).getByText('Not built yet')).toBeInTheDocument()
  })

  it('is reached from Failure signatures, and has no accessibility violations', async () => {
    const user = userEvent.setup()
    open('/next/azure/signatures')
    await user.click(await screen.findByRole('link', { name: 'How autonomy is earned →' }))
    const panel = await screen.findByRole('dialog')
    await within(panel).findByRole('table', { name: 'Which components can earn autonomy' })
    await expectNoAxeViolations(panel)
  })
})
