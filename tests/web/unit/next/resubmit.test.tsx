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

const RUN = '/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.Logic/workflows/orders/runs/run-1'
const logicApps = (offers: boolean): coverageApi.CoverageRow => ({
  id: 'logic-apps.failed-runs', componentKey: 'logic-apps', cloud: 'azure', cloudName: 'Azure', name: 'Logic Apps', stuckPlace: 'Failed runs', detection: 'runs',
  detectionWords: '', movesOnBy: 'A person asks Azure to resubmit the run', status: offers ? 'supported' : 'beingValidated', statusWords: '', note: '', class: offers ? 'r3CloudRedriveOnly' : 'r0Unknown',
  offersReplay: false, offersCloudRedrive: offers, mayEarnAutonomy: false, inSidebar: true, entityKinds: [], planUnit: 'PP4d',
})
const azure = { id: 'ns-az', name: 'contoso', displayName: 'Contoso', provider: 'azure', environment: 'dev', authType: 'defaultAzureCredential', awsRegion: null, gcpProjectId: null, isActive: true, capabilities: null } as unknown as namespacesApi.Namespace
const item: sourcesApi.FailedItem = { id: RUN, place: '/subscriptions/x/workflows/orders', atUtc: '2026-10-01T10:00:00Z', status: 'Failed', reasonCode: null, summary: 'Failed: run-1', copy: 'none', consoleUrl: null }
const source: sourcesApi.ComponentSource = { componentKey: 'logic-apps', available: true, reason: null, reasonInList: false, placesReachedLimit: false, placesIncomplete: false, places: [{ id: '/subscriptions/x/workflows/orders', name: 'orders', kind: 'logic app', consoleUrl: null }] }
const dialogUrl = `/next/azure?dialog=redrive&ns=ns-az&component=logic-apps&item=${encodeURIComponent(RUN)}`

function open(url: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}><RouterProvider router={createMemoryRouter([nextRoute], { initialEntries: [url] })} /></QueryClientProvider>)
}

describe('Resubmit a failed Logic Apps run (unit R2)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: [...coverageRows, logicApps(true)] })
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [], generatedAtUtc: '2026-10-18T10:00:00Z' })
    vi.mocked(enablementApi.fetchEnabledComponents).mockResolvedValue([{ namespaceId: 'ns-az', cloud: 'azure', componentKey: 'logic-apps' }])
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
    vi.mocked(namespacesApi.fetchNamespaces).mockResolvedValue([azure])
    vi.mocked(namespacesApi.fetchNamespace).mockResolvedValue(azure)
    vi.mocked(sourcesApi.fetchComponentSource).mockResolvedValue(source)
    vi.mocked(sourcesApi.fetchFailedItems).mockResolvedValue({ componentKey: 'logic-apps', items: [item], reachedLimit: false, incomplete: false })
    vi.mocked(sourcesApi.fetchFailedItem).mockResolvedValue({ item, reason: 'ActionFailed', cause: 'boom', startedUtc: null, stoppedUtc: null })
    vi.mocked(sourcesApi.fetchRedriveCheck).mockResolvedValue({ canRedrive: true, reasonCode: null, message: 'A person can ask Azure to resubmit this run.', verb: 'resubmit', previousRequestAtUtc: null })
  })

  it('draws a Resubmit… link, not a Redrive… link, and the footnote says every step runs again', async () => {
    open('/next/azure/c/logic-apps/ns-az')
    const link = await screen.findByRole('link', { name: 'Resubmit Failed: run-1' })
    expect(link.textContent).toBe('Resubmit…')
    expect(link.getAttribute('href')).toContain(`?dialog=redrive&ns=ns-az&component=logic-apps&item=${encodeURIComponent(RUN)}`)
    expect(screen.queryByRole('link', { name: /Redrive/ })).toBeNull()
    expect(screen.getAllByText(/every step runs again/).length).toBeGreaterThan(0)
  })

  it('tells the truth in the dialog: a new run, every step again, the original stays failed, never "from the step that failed"', async () => {
    open(dialogUrl)

    expect(await screen.findByRole('button', { name: 'Ask Azure to resubmit' })).toBeEnabled()
    expect(screen.getByText(/starts a new run from the trigger/)).toBeInTheDocument()
    expect(screen.getByText(/Every step runs again, including steps that already succeeded/)).toBeInTheDocument()
    expect(screen.getByText(/It stays failed/)).toBeInTheDocument()
    expect(screen.getByText(/Steps that send, write or charge outside the cloud will do it again/)).toBeInTheDocument()
    expect(screen.queryByText(/from the step that failed/)).toBeNull()
    expect(sourcesApi.redriveRun).not.toHaveBeenCalled()
    await expectNoAxeViolations(document.body)
  })

  it('warns, but does not block, when the cloud already accepted a resubmit of the same run', async () => {
    vi.mocked(sourcesApi.fetchRedriveCheck).mockResolvedValue({ canRedrive: true, reasonCode: null, message: 'A person can ask Azure to resubmit this run.', verb: 'resubmit', previousRequestAtUtc: '2026-10-01T10:30:00Z' })
    open(dialogUrl)

    expect(await screen.findByText(/already accepted a resubmit of this run/)).toBeInTheDocument()
    expect(screen.getByText(/start yet another full run/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Ask Azure to resubmit' })).toBeEnabled()
  })

  it('shows no warning for a run that was never requested', async () => {
    open(dialogUrl)

    await screen.findByRole('button', { name: 'Ask Azure to resubmit' })
    expect(screen.queryByText(/already accepted/)).toBeNull()
  })

  it('an accepted resubmit says what the cloud accepted, calls it Unverified, and never calls the run fixed', async () => {
    vi.mocked(sourcesApi.redriveRun).mockResolvedValue({ entryId: 'e', operationId: 'o', result: 'accepted', state: 'Unverified', message: 'Azure accepted the resubmit request. ServiceHub cannot confirm the run finished fixed.', errorCode: null })
    open(dialogUrl)

    await userEvent.click(await screen.findByRole('button', { name: 'Ask Azure to resubmit' }))

    expect(await screen.findByText('Azure accepted the request.')).toBeInTheDocument()
    expect(screen.getByText(/cannot confirm the run finished fixed/)).toBeInTheDocument()
    expect(sourcesApi.redriveRun).toHaveBeenCalledWith('ns-az', 'logic-apps', RUN)
  })

  it('a refusal from the gate leaves the button disabled and shows its reason', async () => {
    vi.mocked(sourcesApi.fetchRedriveCheck).mockResolvedValue({ canRedrive: false, reasonCode: 'CONNECTION_READ_ONLY', message: 'This connection is marked read-only, so ServiceHub will not change anything in it.', verb: 'resubmit', previousRequestAtUtc: null })
    open(dialogUrl)

    expect(await screen.findByText(/marked read-only/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Ask Azure to resubmit' })).toBeDisabled()
  })
})
