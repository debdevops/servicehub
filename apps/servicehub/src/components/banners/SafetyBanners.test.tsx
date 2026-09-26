import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as agents from '../../lib/api/agents'
import * as identity from '../../lib/api/identity'
import * as settings from '../../lib/api/settings'
import { DemoBanner } from './DemoEntry'
import { SafetyBanners } from './SafetyBanners'
import { enterDemo, leaveDemo } from '../../lib/demo/state'

vi.mock('../../lib/api/settings')
vi.mock('../../lib/api/agents')
vi.mock('../../lib/api/identity', async (original) => ({ ...(await original<typeof identity>()), fetchMe: vi.fn() }))

const wrap = (ui: React.ReactNode) => render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><MemoryRouter>{ui}</MemoryRouter></QueryClientProvider>)

describe('safety banners', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(agents.fetchAgents).mockResolvedValue([])
    vi.mocked(identity.fetchMe).mockResolvedValue({ ownerId: 'o', authMethod: 'session', actor: { identity: 's', kind: 'user', label: 'l', isSession: true }, effectiveRole: 'Admin' })
  })

  it('shows emergency stop while it is on', async () => {
    vi.mocked(settings.fetchEmergencyStop).mockResolvedValue({ active: true, by: 'Dana', at: '2026-09-26T10:00:00Z', reason: 'incident' })
    wrap(<SafetyBanners />)
    expect(await screen.findByRole('alert')).toHaveTextContent(/Emergency stop is on/)
  })

  it('never lets a failed check read as "off" — it says it could not check (6.1)', async () => {
    vi.mocked(settings.fetchEmergencyStop).mockRejectedValue(new Error('down'))
    wrap(<SafetyBanners />)
    expect(await screen.findByText(/couldn’t check whether emergency stop is on/)).toBeInTheDocument()
  })

  it('marks demo data on every screen while demo mode is on (6.5)', () => {
    enterDemo()
    wrap(<DemoBanner />)
    expect(screen.getByRole('status')).toHaveTextContent(/Everything here is made up/)
    leaveDemo()
  })
})
