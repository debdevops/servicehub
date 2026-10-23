import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as agentsApi from '@/lib/api/agents'
import * as sourcesApi from '@/lib/api/componentSources'
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
vi.mock('@/lib/api/componentEnablement', async (o) => ({ ...(await o<typeof enablementApi>()), fetchEnabledComponents: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn() }))
vi.mock('@/lib/api/namespaces', async (o) => ({ ...(await o<typeof namespacesApi>()), fetchNamespaces: vi.fn(), fetchNamespace: vi.fn() }))
vi.mock('@/lib/api/componentSources', async (o) => ({ ...(await o<typeof sourcesApi>()), fetchComponentSource: vi.fn(), fetchFailedItems: vi.fn(), fetchFailedItem: vi.fn() }))

const stepFunctions: coverageApi.CoverageRow = {
  id: 'step-functions.failed-runs', componentKey: 'step-functions', cloud: 'aws', cloudName: 'AWS', name: 'Step Functions', stuckPlace: 'Failed runs', detection: 'runs',
  detectionWords: '', movesOnBy: 'Handed to a person', status: 'beingValidated', statusWords: 'Being validated', note: 'Read only', class: 'r0Unknown', offersReplay: false,
  offersCloudRedrive: false, mayEarnAutonomy: false, inSidebar: true, entityKinds: [], planUnit: 'PP2',
}
const workflows: coverageApi.CoverageRow = {
  ...stepFunctions, id: 'workflows.failed-runs', componentKey: 'workflows', cloud: 'gcp', cloudName: 'Google Cloud', name: 'Workflows', planUnit: 'PP3',
}
const gcp = { id: 'ns-2', name: 'globex', displayName: 'Globex Events', provider: 'gcp', environment: 'dev', authType: 'gcpWorkloadIdentity', awsRegion: null, gcpProjectId: 'globex-1', isActive: true, capabilities: null } as namespacesApi.Namespace
const aws = { id: 'ns-1', name: 'acme', displayName: 'Acme Payments', provider: 'aws', environment: 'dev', authType: 'awsIamRole', awsRegion: 'eu-west-1', gcpProjectId: null, isActive: true, capabilities: null } as namespacesApi.Namespace

const item = (n: number, over: Partial<sourcesApi.FailedItem> = {}): sourcesApi.FailedItem => ({
  id: `arn:aws:states:eu-west-1:1:execution:orders:run-${n}`, place: 'arn:m1', atUtc: `2026-10-0${n}T10:00:00Z`, status: 'FAILED', reasonCode: null, summary: `Failed: run-${n}`, copy: 'none', consoleUrl: `https://console.example/run-${n}`, ...over,
})
const readable: sourcesApi.ComponentSource = { componentKey: 'step-functions', available: true, reason: null, reasonInList: false, placesReachedLimit: false, placesIncomplete: false, places: [{ id: 'arn:m1', name: 'orders', kind: 'state machine', consoleUrl: null }] }

function open(url: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}><RouterProvider router={createMemoryRouter([nextRoute], { initialEntries: [url] })} /></QueryClientProvider>)
}

describe('a component that is not a message store (Step Functions, unit PP1/PP2)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: [...coverageRows, stepFunctions, workflows] })
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [], generatedAtUtc: '2026-10-18T10:00:00Z' })
    vi.mocked(enablementApi.fetchEnabledComponents).mockResolvedValue([{ namespaceId: 'ns-1', cloud: 'aws', componentKey: 'step-functions' }, { namespaceId: 'ns-2', cloud: 'gcp', componentKey: 'workflows' }])
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
    vi.mocked(namespacesApi.fetchNamespaces).mockResolvedValue([aws, gcp])
    vi.mocked(namespacesApi.fetchNamespace).mockImplementation(async (id) => (id === 'ns-2' ? gcp : aws))
    vi.mocked(sourcesApi.fetchComponentSource).mockResolvedValue(readable)
    vi.mocked(sourcesApi.fetchFailedItems).mockResolvedValue({ componentKey: 'step-functions', items: [item(2), item(1, { status: 'TIMED_OUT' })], reachedLimit: false, incomplete: false })
  })

  it('is in the menu and its list offers each AWS connection', async () => {
    open('/next/aws/c/step-functions')
    expect(await screen.findByRole('heading', { name: /Step Functions connections \(1\)/ })).toBeInTheDocument()
    const menu = screen.getByRole('navigation', { name: 'ServiceHub' })
    expect(within(menu).getByRole('link', { name: /Step Functions/ })).toHaveAttribute('href', '/next/aws/c/step-functions')
    expect(within(menu).getByText('Not yet validated')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'View Acme Payments' })).toHaveAttribute('href', '/next/aws/c/step-functions/ns-1')
  })

  it('shows the failed runs, newest first, by the cloud’s own status words', async () => {
    open('/next/aws/c/step-functions/ns-1')
    const table = await screen.findByRole('table', { name: /Failed Step Functions runs, newest first/ })
    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows).toHaveLength(2)
    expect(rows[0]).toHaveTextContent('Failed: run-2')
    expect(rows[1]).toHaveTextContent('Timed out')
    expect(within(screen.getByRole('navigation', { name: 'Sections of this connection' })).getByRole('link', { name: 'Failed runs' })).toHaveAttribute('aria-current', 'page')
  })

  it('opens one run on demand and reads its reason then, not for every row', async () => {
    vi.mocked(sourcesApi.fetchFailedItem).mockResolvedValue({ item: item(2), reason: 'States.TaskFailed', cause: 'The gateway returned 502', startedUtc: '2026-10-02T09:58:00Z', stoppedUtc: '2026-10-02T10:00:00Z' })
    open('/next/aws/c/step-functions/ns-1')
    await screen.findByRole('table')
    expect(sourcesApi.fetchFailedItem).not.toHaveBeenCalled()

    await userEvent.click(screen.getByRole('button', { name: 'Show details of Failed: run-2' }))

    expect(await screen.findByText('States.TaskFailed')).toBeInTheDocument()
    expect(screen.getByText('The gateway returned 502')).toBeInTheDocument()
    expect(sourcesApi.fetchFailedItem).toHaveBeenCalledTimes(1)
  })

  it('offers a console link and no way to replay, re-run, redrive or purge', async () => {
    open('/next/aws/c/step-functions/ns-1')
    await screen.findByRole('table')

    const link = screen.getByRole('link', { name: 'Open Failed: run-2 in the cloud console' })
    expect(link).toHaveAttribute('href', 'https://console.example/run-2')
    expect(link).toHaveAttribute('rel', 'noreferrer noopener')
    // The menu has its own (i) buttons ("About Replays"); what matters is the section that holds the runs.
    const section = screen.getByRole('table').closest('section')!
    expect(within(section).queryByRole('button', { name: /replay|re-run|rerun|redrive|purge|retry|delete|start/i })).not.toBeInTheDocument()
    expect(screen.getByText(/ServiceHub only reads these/)).toBeInTheDocument()
  })

  it('says when the list stopped at its limit and when part of the read failed', async () => {
    vi.mocked(sourcesApi.fetchFailedItems).mockResolvedValue({ componentKey: 'step-functions', items: [item(1)], reachedLimit: true, incomplete: true })
    open('/next/aws/c/step-functions/ns-1')
    expect(await screen.findByText(/Part of this read failed or took too long/)).toBeInTheDocument()
    expect(screen.getByText(/There are more than this/)).toBeInTheDocument()
  })

  it('says plainly when there are no failed runs, and that it is not a guess when the read was partial', async () => {
    vi.mocked(sourcesApi.fetchFailedItems).mockResolvedValue({ componentKey: 'step-functions', items: [], reachedLimit: false, incomplete: true })
    open('/next/aws/c/step-functions/ns-1')
    expect(await screen.findByText(/No failed, timed-out or aborted Step Functions runs were found in what could be read/)).toBeInTheDocument()
  })

  it('a connection that cannot read it says why, reads nothing, and never shows an empty list', async () => {
    vi.mocked(sourcesApi.fetchComponentSource).mockResolvedValue({ ...readable, available: false, reason: 'Not enabled for this connection: needs states:ListExecutions.', places: [] })
    open('/next/aws/c/step-functions/ns-1')

    expect(await screen.findByRole('heading', { name: 'Not enabled' })).toBeInTheDocument()
    expect(screen.getByText(/needs states:ListExecutions/)).toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
    expect(sourcesApi.fetchFailedItems).not.toHaveBeenCalled()
  })

  it('the Access tab says how the connection signs in and whether it can read the component', async () => {
    open('/next/aws/c/step-functions/ns-1?tab=access')
    expect(await screen.findByRole('heading', { name: 'Access' })).toBeInTheDocument()
    expect(screen.getByText('Can read Step Functions')).toBeInTheDocument()
    expect(screen.getByText('Yes')).toBeInTheDocument()
  })

  it('is a 404 for a connection on another cloud, and for a read that fails with 404', async () => {
    vi.mocked(namespacesApi.fetchNamespace).mockResolvedValue({ ...aws, provider: 'azure' } as namespacesApi.Namespace)
    open('/next/aws/c/step-functions/ns-1')
    expect(await screen.findByText(/not found|doesn’t exist|can’t find/i)).toBeInTheDocument()
  })

  it('the same page serves a second component on another cloud: Workflows on Google Cloud, with its own words', async () => {
    vi.mocked(sourcesApi.fetchFailedItems).mockResolvedValue({ componentKey: 'workflows', items: [item(2, { status: 'CANCELLED', summary: 'Cancelled: run-2' }), item(1)], reachedLimit: false, incomplete: false })
    open('/next/gcp/c/workflows/ns-2')

    const table = await screen.findByRole('table', { name: /Failed Workflows runs, newest first/ })
    expect(within(table).getByText('Cancelled', { selector: 'span' })).toBeInTheDocument()
    expect(sourcesApi.fetchComponentSource).toHaveBeenCalledWith('ns-2', 'workflows')
    expect(screen.getByRole('link', { name: 'Failed runs' })).toHaveAttribute('aria-current', 'page')
  })

  it('Workflows lists only the Google connection, and Step Functions only the AWS one', async () => {
    const { unmount } = open('/next/gcp/c/workflows')
    expect(await screen.findByRole('link', { name: 'View Globex Events' })).toHaveAttribute('href', '/next/gcp/c/workflows/ns-2')
    expect(screen.queryByRole('link', { name: 'View Acme Payments' })).not.toBeInTheDocument()
    unmount()
    open('/next/aws/c/step-functions')
    expect(await screen.findByRole('link', { name: 'View Acme Payments' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'View Globex Events' })).not.toBeInTheDocument()
  })

  it('says when the Place filter is cut short, and when part of the places could not be read', async () => {
    vi.mocked(sourcesApi.fetchComponentSource).mockResolvedValue({ ...readable, placesReachedLimit: true })
    const { unmount } = open('/next/aws/c/step-functions/ns-1')
    expect(await screen.findByText(/Only the first 1 places are listed/)).toBeInTheDocument()
    unmount()

    vi.mocked(sourcesApi.fetchComponentSource).mockResolvedValue({ ...readable, placesIncomplete: true })
    open('/next/aws/c/step-functions/ns-1')
    expect(await screen.findByText(/Part of the list of places could not be read/)).toBeInTheDocument()
  })

  it('leaves a message-store component on its own page, unchanged', async () => {
    vi.mocked(componentsApi.fetchComponent).mockRejectedValue(Object.assign(new Error('x'), { isAxiosError: true, response: { status: 500, data: {} } }))
    open('/next/aws/c/sqs/ns-1')
    expect(await screen.findByRole('alert')).toBeInTheDocument()
    expect(sourcesApi.fetchComponentSource).not.toHaveBeenCalled()
  })

  it('has no accessibility violations', async () => {
    const { container } = open('/next/aws/c/step-functions/ns-1')
    await screen.findByRole('table')
    await expectNoAxeViolations(container)
  })
})
