import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
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
vi.mock('@/lib/api/componentSources', async (o) => ({
  ...(await o<typeof sourcesApi>()), fetchComponentSource: vi.fn(), fetchFailedItems: vi.fn(), fetchFailedItem: vi.fn(), fetchRedriveCheck: vi.fn(), redriveRun: vi.fn(),
}))

const RUN = 'arn:aws:states:eu-west-1:1:execution:orders:run-1'
const stepFunctions = (offers: boolean): coverageApi.CoverageRow => ({
  id: 'step-functions.failed-runs', componentKey: 'step-functions', cloud: 'aws', cloudName: 'AWS', name: 'Step Functions', stuckPlace: 'Failed runs', detection: 'runs',
  detectionWords: '', movesOnBy: 'Handed to a person', status: offers ? 'supported' : 'beingValidated', statusWords: '', note: 'Read only', class: offers ? 'r3CloudRedriveOnly' : 'r0Unknown',
  offersReplay: false, offersCloudRedrive: offers, mayEarnAutonomy: false, inSidebar: true, entityKinds: [], planUnit: 'PP2',
})
const aws = { id: 'ns-1', name: 'acme', displayName: 'Acme Payments', provider: 'aws', environment: 'dev', authType: 'awsIamRole', awsRegion: 'eu-west-1', gcpProjectId: null, isActive: true, capabilities: null } as namespacesApi.Namespace
const item: sourcesApi.FailedItem = { id: RUN, place: 'arn:m1', atUtc: '2026-10-01T10:00:00Z', status: 'FAILED', reasonCode: null, summary: 'Failed: run-1', copy: 'none', consoleUrl: null }
const source: sourcesApi.ComponentSource = { componentKey: 'step-functions', available: true, reason: null, reasonInList: false, placesReachedLimit: false, placesIncomplete: false, places: [{ id: 'arn:m1', name: 'orders', kind: 'state machine', consoleUrl: null }] }
const dialogUrl = `/next/aws?dialog=redrive&ns=ns-1&component=step-functions&item=${encodeURIComponent(RUN)}`

function open(url: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}><RouterProvider router={createMemoryRouter([nextRoute], { initialEntries: [url] })} /></QueryClientProvider>)
}

function arrange(offers: boolean, role: 'Admin' | 'Viewer' = 'Admin') {
  vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: [...coverageRows, stepFunctions(offers)] })
  vi.mocked(identityApi.fetchMe).mockResolvedValue(role === 'Admin' ? me() : me({ governanceActive: true, effectiveRole: 'Viewer', recoverRole: 'Viewer' }))
}

describe('Redrive a failed run (unit PP5)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [], generatedAtUtc: '2026-10-18T10:00:00Z' })
    vi.mocked(enablementApi.fetchEnabledComponents).mockResolvedValue([{ namespaceId: 'ns-1', cloud: 'aws', componentKey: 'step-functions' }])
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
    vi.mocked(namespacesApi.fetchNamespaces).mockResolvedValue([aws])
    vi.mocked(namespacesApi.fetchNamespace).mockResolvedValue(aws)
    vi.mocked(sourcesApi.fetchComponentSource).mockResolvedValue(source)
    vi.mocked(sourcesApi.fetchFailedItems).mockResolvedValue({ componentKey: 'step-functions', items: [item], reachedLimit: false, incomplete: false })
    vi.mocked(sourcesApi.fetchFailedItem).mockResolvedValue({ item, reason: 'States.TaskFailed', cause: 'boom', startedUtc: null, stoppedUtc: null })
    vi.mocked(sourcesApi.fetchRedriveCheck).mockResolvedValue({ canRedrive: true, reasonCode: null, message: 'A person can ask AWS to redrive this run.' })
  })

  it('draws no Redrive link while the catalogue row offers none, however much the caller may do', async () => {
    arrange(false)
    open('/next/aws/c/step-functions/ns-1')
    expect(await screen.findByRole('table', { name: /Failed Step Functions runs/ })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /Redrive/ })).toBeNull()
  })

  it('draws the link, carrying the connection, component and run, once the row offers it and the caller is an Approver', async () => {
    arrange(true)
    open('/next/aws/c/step-functions/ns-1')
    const link = await screen.findByRole('link', { name: 'Redrive Failed: run-1' })
    expect(link.getAttribute('href')).toContain(`?dialog=redrive&ns=ns-1&component=step-functions&item=${encodeURIComponent(RUN)}`)
  })

  it('draws no link for a Viewer', async () => {
    arrange(true, 'Viewer')
    open('/next/aws/c/step-functions/ns-1')
    expect(await screen.findByRole('table', { name: /Failed Step Functions runs/ })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /Redrive/ })).toBeNull()
  })

  it('says what will happen and what it cannot promise, and sends nothing until the one button is pressed', async () => {
    arrange(true)
    open(dialogUrl)
    expect(await screen.findByRole('button', { name: 'Ask AWS to redrive' })).toBeEnabled()
    expect(screen.getByText(/never reads the run’s input/)).toBeInTheDocument()
    expect(screen.getByText(/The record will say/)).toBeInTheDocument()
    expect(sourcesApi.redriveRun).not.toHaveBeenCalled()
    await expectNoAxeViolations(document.body)
  })

  it('an accepted redrive says what the cloud accepted, calls it Unverified, and never calls the run fixed', async () => {
    arrange(true)
    vi.mocked(sourcesApi.redriveRun).mockResolvedValue({ entryId: 'e', operationId: 'o', result: 'accepted', state: 'Unverified', message: 'AWS accepted the redrive request. ServiceHub cannot confirm the run finished fixed.', errorCode: null })
    open(dialogUrl)

    await userEvent.click(await screen.findByRole('button', { name: 'Ask AWS to redrive' }))

    expect(await screen.findByText('AWS accepted the request.')).toBeInTheDocument()
    expect(screen.getByText(/cannot confirm the run finished fixed/)).toBeInTheDocument()
    expect(screen.getByText('Unverified')).toBeInTheDocument()
    expect(sourcesApi.redriveRun).toHaveBeenCalledTimes(1)
    expect(sourcesApi.redriveRun).toHaveBeenCalledWith('ns-1', 'step-functions', RUN)
    expect(screen.queryByText(/fixed\b(?!\.)/)).toBeNull()
  })

  it('an unknown answer tells the person not to ask again before checking the run', async () => {
    arrange(true)
    vi.mocked(sourcesApi.redriveRun).mockResolvedValue({ entryId: 'e', operationId: 'o', result: 'unknown', state: 'ExecutionUnknown', message: 'ServiceHub lost contact. Check the run before asking again.', errorCode: 'X' })
    open(dialogUrl)

    await userEvent.click(await screen.findByRole('button', { name: 'Ask AWS to redrive' }))

    expect(await screen.findByText(/Do not ask again until you have checked the run/)).toBeInTheDocument()
  })

  it('a refusal from the gate leaves the button disabled and shows its reason beside it', async () => {
    arrange(true)
    vi.mocked(sourcesApi.fetchRedriveCheck).mockResolvedValue({ canRedrive: false, reasonCode: 'REDRIVE_NOT_OFFERED', message: 'Redrive is not offered for this component yet.' })
    open(dialogUrl)

    expect(await screen.findByText('Redrive is not offered for this component yet.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Ask AWS to redrive' })).toBeDisabled()
  })

  it('a Viewer sees the reason and cannot press the button, even when the gate would allow it', async () => {
    arrange(true, 'Viewer')
    open(dialogUrl)

    await waitFor(() => expect(screen.getByRole('button', { name: 'Ask AWS to redrive' })).toBeDisabled())
    expect(screen.getByText(/you need the Approver role/)).toBeInTheDocument()
  })

  it('a run that is gone says so and offers nothing', async () => {
    arrange(true)
    vi.mocked(sourcesApi.fetchFailedItem).mockResolvedValue({ item: null, reason: null, cause: null, startedUtc: null, stoppedUtc: null })
    open(dialogUrl)

    expect(await screen.findByText(/The run is no longer there/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Ask AWS to redrive' })).toBeNull()
  })
})
