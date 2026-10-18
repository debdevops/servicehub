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
import { auditActionWords } from '@/lib/auditWords'
import { nextRoute } from '@/next/router'
import { expectNoAxeViolations } from '@tests/support/axe'
import { agent, component, coverageRows, me, page } from './homeData'

vi.mock('@/lib/api/coverage', async (o) => ({ ...(await o<typeof coverageApi>()), fetchCoverage: vi.fn() }))
vi.mock('@/lib/api/components', async (o) => ({ ...(await o<typeof componentsApi>()), fetchComponent: vi.fn(), fetchComponents: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn(), fetchAudit: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn() }))
vi.mock('@/lib/api/pendingWork', async (o) => ({ ...(await o<typeof pendingApi>()), fetchPendingWork: vi.fn() }))
vi.mock('@/lib/api/namespaces', async (o) => ({ ...(await o<typeof namespacesApi>()), fetchNamespace: vi.fn(), fetchEntities: vi.fn(), fetchNamespaces: vi.fn() }))

const at = '2026-10-18T09:00:00Z'
const ok = '/next/azure/c/service-bus/ns-azure?tab=audit'
const entry = (id: string, over: Partial<identityApi.AuditEntry> = {}): identityApi.AuditEntry => ({
  id, timestamp: at, actor: { identity: 'dana', kind: 'user', label: 'Dana (Approver)', isSession: false }, action: 'Replay.Message', outcome: 'Success', namespaceId: 'ns-azure',
  namespaceName: 'orders-sb', cloudProvider: 'azure', environment: 'Dev', resourceName: 'orders-queue', errorDetails: null, correlationId: null, ...over,
})
const result = (items: identityApi.AuditEntry[], total = items.length): identityApi.AuditPage => ({ items, page: 1, pageSize: 25, total })
const lastAudit = () => vi.mocked(identityApi.fetchAudit).mock.calls.at(-1)?.[0]

function open(url = ok) {
  const c = component()
  vi.mocked(componentsApi.fetchComponent).mockResolvedValue(c)
  vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [c], generatedAtUtc: at })
  vi.mocked(namespacesApi.fetchNamespace).mockResolvedValue({ id: 'ns-azure', name: 'orders-sb', displayName: null, provider: 'azure', capabilities: { supportsMessageCounts: true, supportsRepeatablePeek: true, countsAreApproximate: false } } as unknown as namespacesApi.Namespace)
  vi.mocked(namespacesApi.fetchEntities).mockResolvedValue({ namespaceId: 'ns-azure', entities: [] })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter([nextRoute], { initialEntries: [url] })
  render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>)
  return router
}

describe('the Audit tab (unit D7)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([]))
    vi.mocked(identityApi.fetchAudit).mockResolvedValue(result([entry('a1'), entry('a2', { action: 'Governance.Grant', resourceName: 'orders-sb', actor: { identity: 'admin', kind: 'user', label: 'Admin', isSession: false } })]))
  })

  it('asks for this connection’s audit trail only, 25 a page', async () => {
    open()
    await screen.findByRole('heading', { name: 'What happened here' })
    expect(lastAudit()).toMatchObject({ namespaceId: 'ns-azure', page: 1, pageSize: 25 })
    expect(lastAudit()?.action).toBeUndefined()
    expect(screen.getByRole('link', { name: 'Audit' })).toHaveAttribute('aria-current', 'page')
  })

  it('says who did what to what, in the words used everywhere else', async () => {
    open()
    const table = await screen.findByRole('table', { name: /What happened on .*, newest first/ })
    const row = within(table).getAllByRole('row')[1]
    expect(row).toHaveTextContent('Dana (Approver)')
    expect(row).toHaveTextContent(auditActionWords['Replay.Message'])
    expect(row).toHaveTextContent('orders-queue')
    expect(within(row).getByText('Done')).toBeInTheDocument()
    expect(within(table).getAllByRole('row')[2]).toHaveTextContent('Granted a role')
  })

  it('a failure says Failed and what was recorded, or that no reason was', async () => {
    vi.mocked(identityApi.fetchAudit).mockResolvedValue(result([
      entry('a1', { outcome: 'Failure', errorDetails: 'The cloud refused it' }),
      entry('a2', { outcome: 'Failure', errorDetails: null }),
    ]))
    open()
    const table = await screen.findByRole('table')
    expect(within(table).getAllByText('Failed')).toHaveLength(2)
    expect(within(table).getByText('The cloud refused it')).toBeInTheDocument()
    expect(within(table).getByText('No reason was recorded')).toBeInTheDocument()
  })

  it('an action ServiceHub has no plain name for is shown as recorded, never hidden or guessed', async () => {
    vi.mocked(identityApi.fetchAudit).mockResolvedValue(result([entry('a1', { action: 'Something.New' })]))
    open()
    expect(await screen.findByText('Something.New')).toBeInTheDocument()
  })

  it('a browser session is shown by its own label, never as a person', async () => {
    vi.mocked(identityApi.fetchAudit).mockResolvedValue(result([entry('a1', { actor: { identity: 'session:abc', kind: 'user', label: 'from this browser session', isSession: true } })]))
    open()
    expect(await screen.findByText('from this browser session')).toBeInTheDocument()
    expect(screen.queryByText('session:abc')).not.toBeInTheDocument()
  })

  it('offers only the filter the API has: an action, in plain words, kept in the address', async () => {
    const router = open(`${ok}&page=3`)
    await screen.findByRole('table')
    const select = screen.getByLabelText('Action')
    expect(within(select).getByRole('option', { name: 'Replayed a message' })).toBeInTheDocument()
    for (const missing of ['Who', 'Entity', 'When']) expect(screen.queryByLabelText(missing), missing).not.toBeInTheDocument()
    await userEvent.selectOptions(select, 'Replay.Message')
    await waitFor(() => expect(lastAudit()).toMatchObject({ action: 'Replay.Message', page: 1 }))
    expect(router.state.location.search).toContain('action=Replay.Message')
    expect(router.state.location.search).not.toContain('page=')
    await userEvent.click(screen.getByRole('button', { name: 'Clear filters' }))
    await waitFor(() => expect(router.state.location.search).not.toContain('action='))
  })

  it('an action in the address that ServiceHub does not know is ignored, never sent as a query', async () => {
    open(`${ok}&action=Drop.Table`)
    await screen.findByRole('table')
    expect(lastAudit()?.action).toBeUndefined()
  })

  it('draws no Export: the API has none', async () => {
    open()
    await screen.findByRole('table')
    expect(screen.queryByRole('button', { name: /export/i })).not.toBeInTheDocument()
  })

  it('pages on the server', async () => {
    vi.mocked(identityApi.fetchAudit).mockResolvedValue(result(Array.from({ length: 25 }, (_, i) => entry(`a${i}`)), 60))
    const router = open()
    expect(await screen.findByText('1–25 of 60')).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Pages of audit entries' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Next page' }))
    await waitFor(() => expect(router.state.location.search).toContain('page=2'))
    await waitFor(() => expect(lastAudit()).toMatchObject({ page: 2 }))
  })

  it('empty states say which kind of empty it is', async () => {
    vi.mocked(identityApi.fetchAudit).mockResolvedValue(result([]))
    const router = open()
    expect(await screen.findByText(/Nothing has been recorded for .* yet\./)).toBeInTheDocument()
    await router.navigate(`${ok}&action=Replay.Message`)
    expect(await screen.findByText('Nothing matches that action.')).toBeInTheDocument()
  })

  it('says it could not read, rather than an empty list that looks like nothing happened', async () => {
    vi.mocked(identityApi.fetchAudit).mockRejectedValue(new Error('down'))
    open()
    expect(await screen.findByText(/The audit trail could not be read/)).toBeInTheDocument()
    expect(screen.queryByText(/Nothing has been recorded/)).not.toBeInTheDocument()
  })

  it('says nothing here can be edited', async () => {
    open()
    expect(await screen.findByText(/Nothing here can be edited\./)).toBeInTheDocument()
  })

  it('has no accessibility violations', async () => {
    open()
    await screen.findByRole('table')
    await expectNoAxeViolations(document.body)
  })
})
