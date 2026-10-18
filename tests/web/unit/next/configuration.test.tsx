import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as agentsApi from '@/lib/api/agents'
import * as componentsApi from '@/lib/api/components'
import * as coverageApi from '@/lib/api/coverage'
import * as identityApi from '@/lib/api/identity'
import * as namespacesApi from '@/lib/api/namespaces'
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
vi.mock('@/lib/api/namespaces', async (o) => ({ ...(await o<typeof namespacesApi>()), fetchNamespaces: vi.fn(), removeNamespace: vi.fn(), testConnection: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({
  ...(await o<typeof settingsApi>()), fetchSettings: vi.fn(), fetchEmergencyStop: vi.fn(), fetchGrants: vi.fn(), setEmergencyStop: vi.fn(),
}))

const at = '2026-10-18T10:00:00Z'
const stopOff = { active: false, by: null, at: null, reason: null }
const base: settingsApi.Settings = {
  notifications: { bellAlwaysOn: true, serverChannel: null, channels: [] },
  security: { apiKeysConfigured: 2, credentialsEncryptedAtRest: true, keyFingerprint: 'abc123' },
  emergencyStop: stopOff,
}
const ns = { id: 'ns-azure', name: 'orders-sb', displayName: null, provider: 'azure', environment: 'dev', lastConnectionTestSucceeded: true, lastConnectionTestAt: at, capabilities: null } as unknown as namespacesApi.Namespace

function open(url: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter([nextRoute], { initialEntries: [url] })
  return { router, ...render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>) }
}

describe('Configuration (unit E9)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [component()], generatedAtUtc: at })
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([]))
    vi.mocked(namespacesApi.fetchNamespaces).mockResolvedValue([ns])
    vi.mocked(settingsApi.fetchSettings).mockResolvedValue(base)
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue(stopOff)
    vi.mocked(settingsApi.fetchGrants).mockResolvedValue([])
  })

  it('opens on Connections, from the same connections read as everywhere, and Add goes to this shell’s Connect dialog', async () => {
    open('/next/configuration')
    await screen.findByRole('heading', { level: 1, name: 'Configuration' })
    expect(await screen.findByText('orders-sb')).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Connections' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('link', { name: '+ Add a cloud' })).toHaveAttribute('href', '/next/configuration?dialog=connect')
  })

  it('switches tabs in the address and shows that section: notifications, access, preferences, backup', async () => {
    const user = userEvent.setup()
    const { router } = open('/next/configuration')
    await screen.findByText('orders-sb')
    await user.click(screen.getByRole('tab', { name: 'Notifications' }))
    expect(router.state.location.search).toBe('?tab=notifications')
    expect(await screen.findByRole('region', { name: 'Notifications' })).toBeInTheDocument()
    await user.click(screen.getByRole('tab', { name: 'Access' }))
    expect(await screen.findByRole('region', { name: 'Access and security' })).toBeInTheDocument()
    await user.click(screen.getByRole('tab', { name: 'Preferences' }))
    expect(await screen.findByRole('region', { name: /Preferences/ })).toBeInTheDocument()
    await user.click(screen.getByRole('tab', { name: 'Backup' }))
    expect(await screen.findByRole('region', { name: /Backup/ })).toBeInTheDocument()
    await user.click(screen.getByRole('tab', { name: 'Connections' }))
    expect(router.state.location.search).toBe('')
  })

  it('opens straight onto a tab from the address', async () => {
    open('/next/configuration?tab=access')
    expect(await screen.findByRole('region', { name: 'Access and security' })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Access' })).toHaveAttribute('aria-selected', 'true')
  })

  it('keeps each control’s own permission: removing a connection is Admin only, and says who can grant it', async () => {
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me({ governanceActive: true, effectiveRole: 'Operator', recoverRole: 'Operator' }))
    open('/next/configuration')
    const remove = await screen.findByRole('button', { name: 'Remove orders-sb' })
    await waitFor(() => expect(remove).toBeDisabled())
    expect(screen.getAllByText(/you need the Admin role/).length).toBeGreaterThan(0)
  })

  it('keeps the emergency stop behind its own confirmation: nothing is sent from opening the tab', async () => {
    open('/next/configuration?tab=access')
    await screen.findByRole('region', { name: 'Access and security' })
    expect(settingsApi.setEmergencyStop).not.toHaveBeenCalled()
  })

  it('shows an error, not an empty tab, when the settings cannot be read', async () => {
    vi.mocked(settingsApi.fetchSettings).mockRejectedValue(new Error('boom'))
    open('/next/configuration?tab=notifications')
    expect(await screen.findByRole('alert')).toHaveTextContent('couldn’t read its settings')
  })

  it('has no accessibility violations', async () => {
    const { container } = open('/next/configuration')
    await screen.findByText('orders-sb')
    await expectNoAxeViolations(container)
  })
})
