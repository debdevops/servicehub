import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as agentsApi from '@/lib/api/agents'
import * as componentsApi from '@/lib/api/components'
import * as enablementApi from '@/lib/api/componentEnablement'
import * as coverageApi from '@/lib/api/coverage'
import * as identityApi from '@/lib/api/identity'
import * as namespacesApi from '@/lib/api/namespaces'
import * as settingsApi from '@/lib/api/settings'
import { nextRoute } from '@/next/router'
import { expectNoAxeViolations } from '@tests/support/axe'
import { agent, coverageRows, me } from './homeData'

vi.mock('@/lib/api/coverage', async (o) => ({ ...(await o<typeof coverageApi>()), fetchCoverage: vi.fn() }))
vi.mock('@/lib/api/components', async (o) => ({ ...(await o<typeof componentsApi>()), fetchComponents: vi.fn(), fetchComponent: vi.fn() }))
vi.mock('@/lib/api/componentEnablement', async (o) => ({ ...(await o<typeof enablementApi>()), fetchComponentList: vi.fn(), fetchEnabledComponents: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn() }))
vi.mock('@/lib/api/namespaces', async (o) => ({ ...(await o<typeof namespacesApi>()), fetchNamespaces: vi.fn(), testConnection: vi.fn(), removeNamespace: vi.fn() }))

const base = { description: null, isActive: true, createdAt: '2026-10-01T00:00:00Z', capabilities: null, environment: 'dev', awsRegion: null, gcpProjectId: null }
const ns = (over: object) => ({ ...base, ...over }) as unknown as namespacesApi.Namespace
const azure = ns({ id: 'ns-az', name: 'orders-sb', displayName: 'Orders bus', provider: 'azure', authType: 'connectionString', lastConnectionTestAt: '2026-10-23T08:00:00Z', lastConnectionTestSucceeded: true })
const aws = ns({ id: 'ns-aws', name: 'acme', displayName: 'Acme Payments', provider: 'aws', authType: 'awsIamRole', awsRegion: 'ap-south-1', lastConnectionTestAt: '2026-10-23T08:00:00Z', lastConnectionTestSucceeded: false })

const entry = (key: string, name: string, state: enablementApi.ComponentState): enablementApi.ComponentEntry => ({
  componentKey: key, name, stuckPlace: 'x', status: 'supported', statusWords: 'Supported', access: 'sameConnection', state, canToggle: false,
  note: null, what: null, appears: null, needs: null, steps: [], snippets: [], permissions: [], includedIn: null,
})

function open(url = '/next/connections') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}><RouterProvider router={createMemoryRouter([nextRoute], { initialEntries: [url] })} /></QueryClientProvider>)
}

describe('Connections: every connection, every provider, in one place', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [], generatedAtUtc: '2026-10-23T10:00:00Z' })
    vi.mocked(enablementApi.fetchEnabledComponents).mockResolvedValue([])
    vi.mocked(enablementApi.fetchComponentList).mockImplementation(async (id) => ({
      namespaceId: id, connectionName: id === 'ns-aws' ? 'Acme Payments' : 'Orders bus', cloud: id === 'ns-aws' ? 'AWS' : 'Azure',
      components: id === 'ns-aws' ? [entry('sqs', 'SQS', 'alwaysOn'), entry('step-functions', 'Step Functions', 'on'), entry('aws-streams', 'Streams', 'unavailable')] : [entry('service-bus', 'Service Bus', 'alwaysOn')],
    }))
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
    vi.mocked(namespacesApi.fetchNamespaces).mockResolvedValue([azure, aws])
  })

  it('is in the menu, lists each cloud with its connections, and says when a cloud has none', async () => {
    open()
    expect(await screen.findByRole('heading', { level: 1, name: /Connections/ })).toBeInTheDocument()
    const menu = screen.getByRole('navigation', { name: 'ServiceHub' })
    expect(within(menu).getByRole('link', { name: /Connections/ })).toHaveAttribute('href', '/next/connections')
    expect(await screen.findByRole('region', { name: 'Azure connections' })).toHaveTextContent('Orders bus')
    expect(screen.getByRole('region', { name: 'AWS connections' })).toHaveTextContent('Acme Payments')
    expect(screen.getByRole('region', { name: 'Google Cloud connections' })).toHaveTextContent(/No Google Cloud connection yet/)
  })

  it('says where each points, how it signs in, whether it connected, and which components are on', async () => {
    open()
    const aws = within(await screen.findByRole('region', { name: 'AWS connections' }))
    expect(await aws.findByText(/ap-south-1 · IAM role · no secret stored/)).toBeInTheDocument()
    expect(aws.getByText('Could not connect')).toBeInTheDocument()
    expect(await aws.findByText('SQS, Step Functions')).toBeInTheDocument()
    const azure = within(screen.getByRole('region', { name: 'Azure connections' }))
    expect(azure.getByText(/orders-sb · Connection string/)).toBeInTheDocument()
    expect(azure.getByText('Connected')).toBeInTheDocument()
  })

  it('offers Connect a cloud, and Add a component for one connection opens the dialog on that connection', async () => {
    open()
    expect(await screen.findByRole('link', { name: /Connect a cloud/ })).toHaveAttribute('href', '/next/connections?dialog=connect')
    expect(await screen.findByRole('link', { name: 'Add a component to Acme Payments' })).toHaveAttribute('href', '/next/connections?dialog=add-component&ns=ns-aws')
  })

  it('opening Add a component from a connection lands on that connection’s cloud, whatever page it is opened over', async () => {
    open('/next/connections?dialog=add-component&ns=ns-aws')
    const dialog = within(await screen.findByRole('dialog', { name: 'Add a component' }))
    expect(await dialog.findByText('Acme Payments')).toBeInTheDocument()
    expect(enablementApi.fetchComponentList).toHaveBeenCalledWith('ns-aws')
  })

  it('Test asks the cloud and says what it found; Remove asks first and keeps the history', async () => {
    vi.mocked(namespacesApi.testConnection).mockResolvedValue({ isConnected: true, message: 'Reached the account.', testedAt: '2026-10-23T09:00:00Z' })
    vi.mocked(namespacesApi.removeNamespace).mockResolvedValue(undefined)
    open()
    await userEvent.click(await screen.findByRole('button', { name: 'Test Acme Payments' }))
    expect(await screen.findByText('Reached the account.')).toBeInTheDocument()
    expect(namespacesApi.testConnection).toHaveBeenCalledWith('ns-aws')

    await userEvent.click(screen.getByRole('button', { name: 'Remove Acme Payments' }))
    expect(screen.getByText(/Its recorded history is kept/)).toBeInTheDocument()
    expect(namespacesApi.removeNamespace).not.toHaveBeenCalled()
  })

  it('a Viewer cannot remove and is told why', async () => {
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me({ governanceActive: true, effectiveRole: 'Viewer', grantors: ['maya'] }))
    open()
    expect(await screen.findByText(/you need the Admin role/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Remove Acme Payments' })).toBeDisabled()
  })

  it('has no accessibility violations', async () => {
    const { container } = open()
    await screen.findByRole('region', { name: 'AWS connections' })
    await expectNoAxeViolations(container)
  })
})
