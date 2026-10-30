import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
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
vi.mock('@/lib/api/components', async (o) => ({ ...(await o<typeof componentsApi>()), fetchComponent: vi.fn(), fetchComponents: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn(), fetchGrants: vi.fn(), fetchSettings: vi.fn() }))
vi.mock('@/lib/api/pendingWork', async (o) => ({ ...(await o<typeof pendingApi>()), fetchPendingWork: vi.fn() }))
vi.mock('@/lib/api/namespaces', async (o) => ({ ...(await o<typeof namespacesApi>()), fetchNamespace: vi.fn(), fetchEntities: vi.fn(), fetchNamespaces: vi.fn(), testConnection: vi.fn(), declareCanSend: vi.fn(), setOwnerChannel: vi.fn() }))

const at = '2026-10-18T09:00:00Z'
const ok = '/next/azure/c/service-bus/ns-azure?tab=access'
const grant = (id: string, over: Partial<settingsApi.Grant> = {}): settingsApi.Grant => ({
  id, granteeIdentity: 'dana', granteeKind: 'User', role: 'Approver', namespaceId: 'ns-azure', pillarKind: null, grantedAt: '2026-10-02T09:00:00Z', grantedByIdentity: 'admin', ...over,
})
const ns = (over: Partial<namespacesApi.Namespace> = {}) => ({
  id: 'ns-azure', name: 'orders-sb', displayName: null, provider: 'azure', environment: 'dev', authType: 'connectionString', awsRegion: null, gcpProjectId: null, isActive: true, createdAt: at,
  lastConnectionTestAt: '2026-10-18T08:00:00Z', lastConnectionTestSucceeded: true, capabilities: { supportsMessageCounts: true, supportsRepeatablePeek: true, countsAreApproximate: false }, ...over,
}) as namespacesApi.Namespace

function open(url = ok, namespace = ns(), c = component()) {
  vi.mocked(componentsApi.fetchComponent).mockResolvedValue(c)
  vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [c], generatedAtUtc: at })
  vi.mocked(namespacesApi.fetchNamespace).mockResolvedValue(namespace)
  vi.mocked(namespacesApi.fetchEntities).mockResolvedValue({ namespaceId: 'ns-azure', entities: [] })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(<QueryClientProvider client={client}><RouterProvider router={createMemoryRouter([nextRoute], { initialEntries: [url] })} /></QueryClientProvider>)
}

describe('the Access tab (unit D5)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me({ governanceActive: true, effectiveRole: 'Admin', recoverRole: 'Admin' }))
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([]))
    vi.mocked(settingsApi.fetchGrants).mockResolvedValue([grant('g1'), grant('g2', { granteeIdentity: 'ops-dashboard', granteeKind: 'ApiKey', role: 'Viewer', namespaceId: null })])
  })

  it('says who the caller is and their role on this connection, preferring a connection-level role over the fleet one', async () => {
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me({ governanceActive: true, effectiveRole: 'Operator', recoverRole: 'Operator', namespaceRecoverRoles: { 'ns-azure': 'Approver' } }))
    open()
    expect(await screen.findByTestId('your-role')).toHaveTextContent('You are maya, with the Approver role here.')
    expect(screen.getByRole('link', { name: 'Access' })).toHaveAttribute('aria-current', 'page')
  })

  it('a browser session is never dressed up as a person', async () => {
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me({ governanceActive: true, recoverRole: 'Admin', actor: { identity: 'session:a58295a6-6c26-46a9-a48c-f5b906235ccc', kind: 'user', label: 'from this browser session', isSession: true } }))
    open()
    const line = await screen.findByTestId('your-role')
    expect(line).toHaveTextContent('You are acting as this browser session, with the Admin role here.')
    expect(line).not.toHaveTextContent('a58295a6')
  })

  it('lists the roles that apply here: this connection’s and the ones that cover everything, in words', async () => {
    open()
    const table = await screen.findByRole('table', { name: /Roles that apply to/ })
    const dana = within(table).getByRole('row', { name: /dana/ })
    expect(dana).toHaveTextContent('Person')
    expect(within(dana).getByText('Approver')).toBeInTheDocument()
    expect(dana).toHaveTextContent('This connection')
    expect(dana).toHaveTextContent('admin')
    const key = within(table).getByRole('row', { name: /ops-dashboard/ })
    expect(key).toHaveTextContent('API key')
    expect(key).toHaveTextContent('Everything')
  })

  it('leaves out a role that is for some other connection', async () => {
    vi.mocked(settingsApi.fetchGrants).mockResolvedValue([grant('g1'), grant('g9', { granteeIdentity: 'other-team', namespaceId: 'ns-elsewhere' })])
    open()
    const table = await screen.findByRole('table', { name: /Roles that apply to/ })
    expect(within(table).queryByText('other-team')).not.toBeInTheDocument()
  })

  it('before any role is granted it says everyone acts as Admin, and asks the grants endpoint for nothing', async () => {
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me({ governanceActive: false }))
    open()
    expect(await screen.findByText(/everyone who reaches this ServiceHub acts as Admin/)).toBeInTheDocument()
    expect(settingsApi.fetchGrants).not.toHaveBeenCalled()
    expect(screen.queryByRole('table', { name: /Roles that apply to/ })).not.toBeInTheDocument()
  })

  it('a non-Admin cannot list the grants, and is told who can rather than shown an empty list', async () => {
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me({ governanceActive: true, effectiveRole: 'Viewer', recoverRole: 'Viewer', grantors: ['maya', 'sam'] }))
    vi.mocked(settingsApi.fetchGrants).mockRejectedValue(new Error('forbidden'))
    open()
    expect(await screen.findByText(/Only an Admin can list who has access. maya, sam can./)).toBeInTheDocument()
    expect(screen.queryByText(/No role is granted/)).not.toBeInTheDocument()
  })

  it('says when no role applies to this connection', async () => {
    vi.mocked(settingsApi.fetchGrants).mockResolvedValue([])
    open()
    expect(await screen.findByText('No role is granted that applies to this connection.')).toBeInTheDocument()
  })

  it('pages the roles 25 at a time', async () => {
    vi.mocked(settingsApi.fetchGrants).mockResolvedValue(Array.from({ length: 30 }, (_, i) => grant(`g${i}`, { granteeIdentity: `user-${String(i).padStart(2, '0')}` })))
    open()
    expect(await screen.findByText('1–25 of 30')).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Pages of roles that apply here' })).toBeInTheDocument()
  })

  it('shows what each role can do, from the server’s own gates', async () => {
    open()
    const matrix = await screen.findByRole('table', { name: 'What each role can do' })
    const operator = within(matrix).getByRole('row', { name: /Operator/ })
    expect(operator).toHaveTextContent('Switch off or delete only')
    expect(within(matrix).getByRole('row', { name: /Viewer/ })).toHaveTextContent('Yes')
    expect(within(within(matrix).getByRole('row', { name: /Admin/ })).getAllByText('Yes')).toHaveLength(5)
  })

  it('describes the connection without ever showing a credential, and never claims the cloud confirmed it may send', async () => {
    open()
    const box = await screen.findByRole('region', { name: 'This connection' })
    expect(box).toHaveTextContent('Connected withConnection string')
    expect(box).toHaveTextContent('Read (listen)Yes')
    expect(box).toHaveTextContent('Send')
    expect(box).toHaveTextContent('Not said. Treated as may send. The cloud itself was not asked')
    expect(box).not.toHaveTextContent(/Send\s*Yes/)
    expect(box).toHaveTextContent('Encrypted on this machine, never shown again')
    expect(box).toHaveTextContent('worked')
  })

  it('says a connection declared read only is read only, and an Admin can allow send', async () => {
    vi.mocked(namespacesApi.declareCanSend).mockResolvedValue(ns({ canSend: true }))
    open(ok, ns({ canSend: false }))
    const box = await screen.findByRole('region', { name: 'This connection' })
    expect(box).toHaveTextContent('Read only, as declared. ServiceHub will not replay or send through this connection.')

    await userEvent.click(within(box).getByRole('button', { name: 'Allow send' }))
    await waitFor(() => expect(namespacesApi.declareCanSend).toHaveBeenCalledWith('ns-azure', true))
  })

  it('a connection declared read and send says the cloud itself was not asked', async () => {
    open(ok, ns({ canSend: true }))
    const box = await screen.findByRole('region', { name: 'This connection' })
    expect(box).toHaveTextContent('Read and send, as declared. The cloud itself was not asked')
    expect(within(box).getByRole('button', { name: 'Make read only' })).toBeInTheDocument()
  })

  it('shows who is told about the connection and lets an Admin choose one enabled channel', async () => {
    vi.mocked(settingsApi.fetchSettings).mockResolvedValue({
      notifications: { bellAlwaysOn: true, serverChannel: null, channels: [
        { id: 'ch-ops', format: 'slack', label: '#ops', enabled: true, createdAt: at, createdBy: 'a', lastDeliveredAt: null, lastError: null },
        { id: 'ch-off', format: 'slack', label: '#off', enabled: false, createdAt: at, createdBy: 'a', lastDeliveredAt: null, lastError: null },
      ] },
      security: { apiKeysConfigured: 0, credentialsEncryptedAtRest: true, keyFingerprint: 'x' },
      emergencyStop: { active: false, by: null, at: null, reason: null },
    })
    vi.mocked(namespacesApi.setOwnerChannel).mockResolvedValue(ns({ ownerChannelId: 'ch-ops' }))
    open()
    const box = await screen.findByRole('region', { name: 'This connection' })
    expect(box).toHaveTextContent('ToldEvery enabled channel')

    const choose = await within(box).findByRole('combobox', { name: 'Tell only' })
    expect(within(choose).queryByRole('option', { name: '#off' })).not.toBeInTheDocument()
    await userEvent.selectOptions(choose, 'ch-ops')
    await waitFor(() => expect(namespacesApi.setOwnerChannel).toHaveBeenCalledWith('ns-azure', 'ch-ops'))
  })

  it('an identity-based connection stores no secret and says so', async () => {
    open(ok, ns({ authType: 'awsOidc' }))
    expect(await screen.findByText('No secret is stored: it signs in as an identity')).toBeInTheDocument()
  })

  it('an unreachable connection says reading is not known, rather than "Yes"', async () => {
    open(ok, ns(), component({ reachable: false, lastError: 'denied' }))
    expect(await screen.findByText('Not known: it cannot be reached')).toBeInTheDocument()
  })

  it('Test again runs the test, says how it went, and never reveals the credential', async () => {
    vi.mocked(namespacesApi.testConnection).mockResolvedValue({ isConnected: false, message: 'Access was denied.', testedAt: '2026-10-18T10:30:00Z' })
    open()
    await userEvent.click(await screen.findByRole('button', { name: 'Test again' }))
    expect(namespacesApi.testConnection).toHaveBeenCalledWith('ns-azure')
    expect(await screen.findByText('Access was denied.')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('region', { name: 'This connection' })).toHaveTextContent('did not work'))
  })

  it('a test that works says so', async () => {
    vi.mocked(namespacesApi.testConnection).mockResolvedValue({ isConnected: true, message: 'ok', testedAt: '2026-10-18T10:30:00Z' })
    open()
    await userEvent.click(await screen.findByRole('button', { name: 'Test again' }))
    expect(await screen.findByText('The connection worked.')).toBeInTheDocument()
  })

  it('Manage access goes to Configuration', async () => {
    open()
    expect(await screen.findByRole('link', { name: 'Manage access' })).toHaveAttribute('href', '/next/configuration')
  })

  it('has no accessibility violations', async () => {
    open()
    await screen.findByRole('table', { name: /Roles that apply to/ })
    await expectNoAxeViolations(document.body)
  })
})
