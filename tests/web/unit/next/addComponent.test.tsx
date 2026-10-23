import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
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
vi.mock('@/lib/api/componentEnablement', async (o) => ({
  ...(await o<typeof enablementApi>()), fetchComponentList: vi.fn(), fetchEnabledComponents: vi.fn(), checkComponent: vi.fn(), setComponent: vi.fn(),
}))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn() }))
vi.mock('@/lib/api/namespaces', async (o) => ({ ...(await o<typeof namespacesApi>()), fetchNamespaces: vi.fn(), fetchNamespace: vi.fn() }))

const aws = { id: 'ns-1', name: 'acme', displayName: 'Acme Payments', provider: 'aws', environment: 'dev', authType: 'awsIamRole', awsRegion: 'eu-west-1', gcpProjectId: null, isActive: true, createdAt: '', lastConnectionTestAt: null, lastConnectionTestSucceeded: true, description: null, capabilities: null } as unknown as namespacesApi.Namespace

const entry = (over: Partial<enablementApi.ComponentEntry>): enablementApi.ComponentEntry => ({
  componentKey: 'x', name: 'X', stuckPlace: 'Somewhere', status: 'supported', statusWords: 'Supported', access: 'sameConnection', state: 'off', canToggle: false,
  note: null, what: null, appears: null, needs: null, steps: [], snippets: [], permissions: [], includedIn: null, ...over,
})
const list: enablementApi.ComponentList = {
  namespaceId: 'ns-1', connectionName: 'Acme Payments', cloud: 'AWS',
  components: [
    entry({ componentKey: 'sqs', name: 'SQS', state: 'alwaysOn', what: 'Messages SQS moved to a dead-letter queue.' }),
    entry({
      componentKey: 'step-functions', name: 'Step Functions', status: 'beingValidated', statusWords: 'Not yet validated', state: 'off', canToggle: true, stuckPlace: 'Failed runs',
      what: 'State machine runs that ended failed.', appears: 'Appears in the menu as Step Functions.', needs: 'states:ListStateMachines and two more.',
      steps: ['Add this policy.', 'Press Check access.'], snippets: [{ label: 'IAM policy', text: '{"Version":"2012-10-17"}' }],
      permissions: ['states:ListStateMachines', 'states:ListExecutions', 'states:DescribeExecution'],
    }),
    entry({ componentKey: 'aws-streams', name: 'Kinesis, DynamoDB streams', status: 'onRequest', statusWords: 'On request', access: 'notBuildable', state: 'unavailable' }),
  ],
}

function open(url: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}><RouterProvider router={createMemoryRouter([nextRoute], { initialEntries: [url] })} /></QueryClientProvider>)
}

const dialog = async () => within(await screen.findByRole('dialog', { name: 'Add a component' }))

describe('Add a component (one dialog: choose, connect, bring in)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [], generatedAtUtc: '2026-10-23T10:00:00Z' })
    vi.mocked(enablementApi.fetchEnabledComponents).mockResolvedValue([])
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
    vi.mocked(namespacesApi.fetchNamespaces).mockResolvedValue([aws])
    vi.mocked(enablementApi.fetchComponentList).mockResolvedValue(list)
  })

  it('lists every cloud’s components, uses the existing connection, shows what is on, and locks what cannot be ticked', async () => {
    open('/next/aws?dialog=add-component')
    const d = await dialog()
    expect(await d.findByText('Acme Payments')).toBeInTheDocument()
    expect(d.getByText(/never asks for a key/)).toBeInTheDocument()
    // Every cloud is listed, not just the one the page is on; a cloud with no connection says so and still names what it can bring in.
    expect(d.getByRole('region', { name: 'AWS components' })).toBeInTheDocument()
    expect(d.getByRole('region', { name: 'Azure components' })).toHaveTextContent(/Connect Azure first/)
    expect(d.getByRole('region', { name: 'Google Cloud components' })).toHaveTextContent(/It can bring in: Pub\/Sub/)
    expect(await d.findByRole('checkbox', { name: 'SQS (Supported)' })).toBeChecked()
    expect(d.getByRole('checkbox', { name: 'SQS (Supported)' })).toBeDisabled()
    expect(d.getByRole('checkbox', { name: 'Kinesis, DynamoDB streams (On request)' })).toBeDisabled()
    expect(d.getByRole('checkbox', { name: 'Step Functions (Not yet validated)' })).toBeEnabled()
    expect(d.getByRole('button', { name: 'Add 0 components' })).toBeDisabled()
  })

  it('ticking opens the row in place with its prerequisites, a copyable policy and a read-only check', async () => {
    open('/next/aws?dialog=add-component')
    const d = await dialog()
    await userEvent.click(await d.findByRole('checkbox', { name: 'Step Functions (Not yet validated)' }))
    expect(d.getByText(/No key to paste/)).toBeInTheDocument()
    expect(d.getByText('Add this policy.')).toBeInTheDocument()
    expect(d.getByText('{"Version":"2012-10-17"}')).toBeInTheDocument()
    expect(d.getByRole('button', { name: 'Add 1 component' })).toBeEnabled()

    vi.mocked(enablementApi.checkComponent).mockResolvedValue({ componentKey: 'step-functions', ok: false, reason: 'Not enabled for this connection: needs states:ListExecutions.', permissions: list.components[1]!.permissions })
    await userEvent.click(d.getByRole('button', { name: 'Check access' }))
    expect(await d.findByText(/needs states:ListExecutions/)).toBeInTheDocument()
    expect(enablementApi.setComponent).not.toHaveBeenCalled()
  })

  it('Add saves the ticked row, says so on the row, and Done closes', async () => {
    vi.mocked(enablementApi.setComponent).mockResolvedValue({ ...list.components[1]!, state: 'on' })
    open('/next/aws?dialog=add-component')
    const d = await dialog()
    await userEvent.click(await d.findByRole('checkbox', { name: 'Step Functions (Not yet validated)' }))
    await userEvent.click(d.getByRole('button', { name: 'Add 1 component' }))
    expect(await d.findByText(/Added\. It is on the menu now/)).toBeInTheDocument()
    expect(enablementApi.setComponent).toHaveBeenCalledWith('ns-1', 'step-functions', true)
    await userEvent.click(d.getByRole('button', { name: 'Done' }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Add a component' })).not.toBeInTheDocument())
  })

  it('a refused row says what is missing, saves nothing for it, and never blocks the others', async () => {
    const problem = Object.assign(new Error('x'), { isAxiosError: true, response: { status: 409, data: { code: 'capability_unavailable', detail: 'Not added. Not enabled for this connection: needs states:ListExecutions.' } } })
    vi.mocked(enablementApi.setComponent).mockRejectedValue(problem)
    open('/next/aws?dialog=add-component')
    const d = await dialog()
    await userEvent.click(await d.findByRole('checkbox', { name: 'Step Functions (Not yet validated)' }))
    await userEvent.click(d.getByRole('button', { name: 'Add 1 component' }))
    expect(await d.findByRole('alert')).toHaveTextContent(/needs states:ListExecutions/)
    expect(d.getByText(/0 saved, 1 not saved/)).toBeInTheDocument()
  })

  it('a Viewer is told why they cannot add, before the click', async () => {
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me({ governanceActive: true, effectiveRole: 'Viewer', grantors: ['maya'] }))
    open('/next/aws?dialog=add-component')
    const d = await dialog()
    expect(await d.findByText(/you need the Admin role/)).toBeInTheDocument()
    expect(d.getByRole('checkbox', { name: 'Step Functions (Not yet validated)' })).toBeDisabled()
  })

  it('a cloud with no connection says so and links to Connect a cloud, never to a key box', async () => {
    vi.mocked(namespacesApi.fetchNamespaces).mockResolvedValue([])
    open('/next/gcp?dialog=add-component')
    const d = await dialog()
    expect(await d.findByText(/Connect Google Cloud first/)).toBeInTheDocument()
    expect(d.getByRole('link', { name: 'Connect Google Cloud' })).toHaveAttribute('href', '/next/gcp?dialog=connect')
    expect(enablementApi.fetchComponentList).not.toHaveBeenCalled()
  })

  it('has no accessibility violations and an (i) on its headings', async () => {
    const { container } = open('/next/aws?dialog=add-component')
    const d = await dialog()
    await d.findByRole('checkbox', { name: 'Step Functions (Not yet validated)' })
    await expectNoAxeViolations(container)
  })
})
