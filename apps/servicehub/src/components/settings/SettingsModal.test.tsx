import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as settings from '../../lib/api/settings'
import * as identity from '../../lib/api/identity'
import type { Me } from '../../lib/api/identity'
import * as namespaces from '../../lib/api/namespaces'
import * as agents from '../../lib/api/agents'
import SettingsModal from './SettingsModal'
import { SafetyBanners } from '../banners/SafetyBanners'

vi.mock('../../lib/api/settings', async (o) => ({ ...(await o<typeof settings>()), fetchSettings: vi.fn(), addChannel: vi.fn(), setEmergencyStop: vi.fn(), fetchEmergencyStop: vi.fn(), fetchGrants: vi.fn() }))
vi.mock('../../lib/api/identity', async (o) => ({ ...(await o<typeof identity>()), fetchMe: vi.fn() }))
vi.mock('../../lib/api/namespaces', async (o) => ({ ...(await o<typeof namespaces>()), fetchNamespaces: vi.fn() }))
vi.mock('../../lib/api/agents', async (o) => ({ ...(await o<typeof agents>()), fetchAgents: vi.fn(), resumeAgent: vi.fn() }))

const stopOff = { active: false, by: null, at: null, reason: null }
const base: settings.Settings = {
  notifications: { bellAlwaysOn: true, serverChannel: null, channels: [] },
  security: { apiKeysConfigured: 2, credentialsEncryptedAtRest: true, keyFingerprint: 'abc123' },
  emergencyStop: stopOff,
}
const admin: Me = { ownerId: 'o', authMethod: 'session', actor: { identity: 'session', kind: 'user', label: 'from this browser session', isSession: true }, effectiveRole: 'Admin' as const, governanceActive: false }

function wrap(ui: React.ReactNode, url = '/') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}><MemoryRouter initialEntries={[url]}>{ui}</MemoryRouter></QueryClientProvider>)
}

describe('Settings', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(settings.fetchSettings).mockResolvedValue(base)
    vi.mocked(identity.fetchMe).mockResolvedValue(admin)
    vi.mocked(namespaces.fetchNamespaces).mockResolvedValue([])
    vi.mocked(settings.fetchGrants).mockResolvedValue([])
  })

  it('shows the bell locked on — it cannot be switched off (R7)', async () => {
    wrap(<SettingsModal />)
    const bell = await screen.findByRole('switch', { name: 'In-app bell is always on' })
    expect(bell).toHaveAttribute('aria-disabled', 'true')
  })

  it('adds a webhook channel with its address hidden as it is typed', async () => {
    vi.mocked(settings.addChannel).mockResolvedValue({} as never)
    wrap(<SettingsModal />)
    await userEvent.click((await screen.findAllByRole('button', { name: '+ Add webhook' }))[0])
    const address = screen.getByLabelText(/Webhook address/)
    expect(address).toHaveAttribute('type', 'password')
    await userEvent.type(screen.getByLabelText(/Name/), '#ops')
    await userEvent.type(address, 'https://hooks.slack.com/x')
    await userEvent.click(screen.getByRole('button', { name: 'Add' }))
    expect(settings.addChannel).toHaveBeenCalledWith({ format: 'slack', label: '#ops', url: 'https://hooks.slack.com/x' }, expect.anything())
  })

  it('switches emergency stop on only with a reason and STOP typed', async () => {
    vi.mocked(settings.setEmergencyStop).mockResolvedValue({ ...stopOff, active: true })
    wrap(<SettingsModal />)
    const on = await screen.findByRole('button', { name: /Switch emergency stop on/ })
    expect(on).toBeDisabled()
    await userEvent.type(screen.getByLabelText('Why'), 'incident')
    expect(on).toBeDisabled()
    await userEvent.type(screen.getByLabelText('Type STOP to confirm'), 'STOP')
    await userEvent.click(on)
    expect(settings.setEmergencyStop).toHaveBeenCalledWith({ active: true, reason: 'incident', confirm: 'STOP' }, expect.anything())
  })
})

describe('Safety banners', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(identity.fetchMe).mockResolvedValue(admin)
    vi.mocked(agents.fetchAgents).mockResolvedValue([])
  })

  it('says what emergency stop really does — automatic actions refused, a person still can', async () => {
    vi.mocked(settings.fetchEmergencyStop).mockResolvedValue({ active: true, by: 'ApiKey:lead', at: new Date().toISOString(), reason: 'incident 42' })
    wrap(<SafetyBanners />)
    const banner = await screen.findByRole('alert')
    expect(banner).toHaveTextContent('ServiceHub will not act on its own')
    expect(banner).toHaveTextContent('a replay a person starts still goes through its checks')
    expect(banner).toHaveTextContent('incident 42')
  })

  it('renders nothing while nothing is true', async () => {
    vi.mocked(settings.fetchEmergencyStop).mockResolvedValue(stopOff)
    const { container } = wrap(<SafetyBanners />)
    await new Promise((r) => setTimeout(r, 50))
    expect(container).toBeEmptyDOMElement()
  })

  it('offers Resume on Simple but only a link on Advanced — Advanced never gives authority back', async () => {
    vi.mocked(settings.fetchEmergencyStop).mockResolvedValue(stopOff)
    vi.mocked(agents.fetchAgents).mockResolvedValue([{ id: 'auto-replay', canAct: true, isPaused: true } as never])
    wrap(<SafetyBanners />, '/advanced/agents')
    expect(await screen.findByText('Resume on Home')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Resume' })).not.toBeInTheDocument()
  })
})
