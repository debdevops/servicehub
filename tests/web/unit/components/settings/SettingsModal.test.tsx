import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as settings from '@/lib/api/settings'
import * as identity from '@/lib/api/identity'
import type { Me } from '@/lib/api/identity'
import * as namespaces from '@/lib/api/namespaces'
import * as agents from '@/lib/api/agents'
import SettingsModal from '@/components/settings/SettingsModal'
import { SafetyBanners } from '@/components/banners/SafetyBanners'

vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settings>()), fetchSettings: vi.fn(), addChannel: vi.fn(), setEmergencyStop: vi.fn(), fetchEmergencyStop: vi.fn(), fetchGrants: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identity>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/namespaces', async (o) => ({ ...(await o<typeof namespaces>()), fetchNamespaces: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agents>()), fetchAgents: vi.fn(), resumeAgent: vi.fn() }))

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

  it('lifts emergency stop only with a reason and LIFT typed — never a single click', async () => {
    const stopOn = { active: true, by: 'from this browser session', at: '2026-09-28T10:00:00Z', reason: 'incident' }
    vi.mocked(settings.fetchSettings).mockResolvedValue({ ...base, emergencyStop: stopOn })
    vi.mocked(settings.setEmergencyStop).mockResolvedValue(stopOff)
    wrap(<SettingsModal />)
    const off = await screen.findByRole('button', { name: /Switch emergency stop off/ })
    expect(off).toBeDisabled()
    await userEvent.click(off)
    expect(settings.setEmergencyStop).not.toHaveBeenCalled()

    await userEvent.type(screen.getByLabelText('Type LIFT to confirm'), 'LIFT')
    expect(off).toBeDisabled() // the word alone is not enough: say why it is safe
    await userEvent.type(screen.getByLabelText('Why it is safe to resume'), 'incident closed')
    expect(off).toBeEnabled()
    await userEvent.click(off)
    expect(settings.setEmergencyStop).toHaveBeenCalledWith({ active: false, reason: 'incident closed', confirm: 'LIFT' }, expect.anything())
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

  describe('confirming fixes', () => {
    const caps = (canProveDlqAbsence: boolean) => ({ canProveDlqAbsence } as namespaces.ProviderCapabilities)
    const ns = (id: string, provider: namespaces.CloudProvider, canProve: boolean): namespaces.Namespace => ({
      id, name: `${provider}-ns`, displayName: null, description: null, provider, environment: 'dev', authType: 'connectionString', awsRegion: null, gcpProjectId: null,
      isActive: true, createdAt: '2026-10-01T00:00:00Z', lastConnectionTestAt: null, lastConnectionTestSucceeded: true, capabilities: caps(canProve),
    })
    const off: namespaces.DlqObserver = {
      needed: true, enabled: false, live: false, observerReference: null, dlqEntityName: null, stalenessBoundMinutes: 30, lastCanarySentAt: null, lastConfirmedAt: null,
      status: 'No observer is set up, so a replay here can be sent back but not confirmed as fixed.', needsReference: false, referenceHint: null,
    }

    it('is offered only to a cloud that cannot tell by itself (asked of its capability, not its name)', async () => {
      vi.mocked(namespaces.fetchNamespaces).mockResolvedValue([ns('a', 'azure', true), ns('b', 'aws', false)])
      const fetch = vi.spyOn(namespaces, 'fetchDlqObserver').mockResolvedValue(off)
      wrap(<SettingsModal />)
      expect(await screen.findByLabelText('Confirming fixes on aws-ns')).toBeInTheDocument()
      expect(screen.queryByLabelText('Confirming fixes on azure-ns')).toBeNull()
      expect(fetch).toHaveBeenCalledTimes(1)
      expect(fetch).toHaveBeenCalledWith('b')
    })

    it('switches on with nothing to name when the cloud reads the queue itself', async () => {
      vi.mocked(namespaces.fetchNamespaces).mockResolvedValue([ns('b', 'aws', false)])
      vi.spyOn(namespaces, 'fetchDlqObserver').mockResolvedValue(off)
      const configure = vi.spyOn(namespaces, 'configureDlqObserver').mockResolvedValue({ ...off, enabled: true, status: 'Turned on, but ServiceHub has not yet been able to see this cloud’s dead letters — not confirming anything.' })
      wrap(<SettingsModal />)
      await userEvent.click(await screen.findByRole('button', { name: 'Switch on' }))
      expect(configure).toHaveBeenCalledWith('b', { enabled: true })
      expect(await screen.findByText(/not confirming anything/)).toBeInTheDocument()
      expect(screen.getByText('On — not working yet')).toBeInTheDocument()
    })

    it('asks for the two names the cloud’s own check wants, and will not switch on without both', async () => {
      vi.mocked(namespaces.fetchNamespaces).mockResolvedValue([ns('g', 'gcp', false)])
      vi.spyOn(namespaces, 'fetchDlqObserver').mockResolvedValue({ ...off, needsReference: true, referenceHint: 'Its name must end with ‘-servicehub-observer’.' })
      const configure = vi.spyOn(namespaces, 'configureDlqObserver').mockResolvedValue({ ...off, enabled: true })
      wrap(<SettingsModal />)
      const on = await screen.findByRole('button', { name: 'Switch on' })
      expect(on).toBeDisabled()
      expect(screen.getByText(/must end with/)).toBeInTheDocument()
      await userEvent.type(screen.getByLabelText(/Subscription ServiceHub reads/), 'q-servicehub-observer')
      expect(on).toBeDisabled()
      await userEvent.type(screen.getByLabelText(/Dead-letter topic/), 'q-topic')
      await userEvent.click(on)
      expect(configure).toHaveBeenCalledWith('g', { enabled: true, observerReference: 'q-servicehub-observer', dlqEntityName: 'q-topic' })
    }, 20_000)

    it('says a check that could not read the queue could not, with the reason', async () => {
      vi.mocked(namespaces.fetchNamespaces).mockResolvedValue([ns('b', 'aws', false)])
      vi.spyOn(namespaces, 'fetchDlqObserver').mockResolvedValue({ ...off, enabled: true })
      vi.spyOn(namespaces, 'checkDlqObserver').mockResolvedValue({ healthy: false, reason: 'PERMISSION_DENIED', complete: null, incompleteReason: null, count: null })
      wrap(<SettingsModal />)
      await userEvent.click(await screen.findByRole('button', { name: 'Check now' }))
      expect(await screen.findByText(/cannot read this cloud’s dead letters right now \(PERMISSION_DENIED\)/)).toBeInTheDocument()
    })
  })
})
