import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as agentsApi from '@/lib/api/agents'
import * as componentsApi from '@/lib/api/components'
import * as coverageApi from '@/lib/api/coverage'
import * as deadLettersApi from '@/lib/api/deadLetters'
import * as identityApi from '@/lib/api/identity'
import * as namespacesApi from '@/lib/api/namespaces'
import * as pendingApi from '@/lib/api/pendingWork'
import * as settingsApi from '@/lib/api/settings'
import { nextRoute } from '@/next/router'
import { expectNoAxeViolations } from '@tests/support/axe'
import { agent, approval, component, coverageRows, handOff, me, page } from './homeData'

vi.mock('@/lib/api/coverage', async (o) => ({ ...(await o<typeof coverageApi>()), fetchCoverage: vi.fn() }))
vi.mock('@/lib/api/components', async (o) => ({ ...(await o<typeof componentsApi>()), fetchComponent: vi.fn(), fetchComponents: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn() }))
vi.mock('@/lib/api/pendingWork', async (o) => ({ ...(await o<typeof pendingApi>()), fetchPendingWork: vi.fn() }))
vi.mock('@/lib/api/deadLetters', async (o) => ({ ...(await o<typeof deadLettersApi>()), fetchDeadLetters: vi.fn() }))
vi.mock('@/lib/api/namespaces', async (o) => ({ ...(await o<typeof namespacesApi>()), fetchNamespace: vi.fn(), fetchEntities: vi.fn(), fetchNamespaces: vi.fn(), lookAtDeadLetters: vi.fn() }))

const at = '2026-10-18T09:00:00Z'
const letter = (id: number, over: Partial<deadLettersApi.DeadLetter> = {}) => ({
  id, namespaceId: 'ns-azure', messageId: `m-${id}`, sequenceNumber: id, entityName: 'orders-queue', entityType: 'queue', topicName: null, detectedAtUtc: at, enqueuedTimeUtc: at,
  deliveryCount: 5, sizeInBytes: 10, deadLetterReason: 'ProcessingError', deadLetterErrorDescription: 'Timed out', status: 'active', ...over,
}) as deadLettersApi.DeadLetter
const result = (items: deadLettersApi.DeadLetter[], total = items.length, over: Partial<deadLettersApi.DeadLetterPage> = {}): deadLettersApi.DeadLetterPage => ({
  items, paging: { total, page: 1, pageSize: 25 }, groups: [{ reason: 'ProcessingError', count: total }], otherReasons: null, entities: ['orders-queue', 'payments-topic/subscriptions/sub-1'], ...over,
})
const ok = '/next/azure/c/service-bus/ns-azure'

function open(url: string, c = component(), watched = true) {
  vi.mocked(componentsApi.fetchComponent).mockResolvedValue(c)
  vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [c], generatedAtUtc: at })
  vi.mocked(namespacesApi.fetchNamespace).mockResolvedValue({
    id: 'ns-azure', name: 'orders-sb', displayName: null, provider: 'azure', environment: 'dev', authType: 'connectionString', awsRegion: null, gcpProjectId: null,
    capabilities: { supportsMessageCounts: true, supportsRepeatablePeek: watched, countsAreApproximate: false },
  } as unknown as namespacesApi.Namespace)
  vi.mocked(namespacesApi.fetchEntities).mockResolvedValue({ namespaceId: 'ns-azure', entities: [] })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter([nextRoute], { initialEntries: [url] })
  render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>)
  return router
}
const lastQuery = () => vi.mocked(deadLettersApi.fetchDeadLetters).mock.calls.at(-1)?.[0]

describe('the Dead-letter messages tab (unit D3)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([]))
    vi.mocked(deadLettersApi.fetchDeadLetters).mockResolvedValue(result([letter(1), letter(2)]))
  })

  it('asks for this connection’s stuck messages, 25 a page, and nothing from any other', async () => {
    open(`${ok}?tab=dead-letters`)
    await screen.findByRole('heading', { name: 'Dead-letter messages (2)' })
    expect(lastQuery()).toMatchObject({ namespaceId: 'ns-azure', status: 'active', page: 1, pageSize: 25 })
    expect(screen.getByRole('link', { name: /Dead-letter messages/ })).toHaveAttribute('aria-current', 'page')
  })

  it('lists each message with its reason, description, deliveries and state, and opens the Message dialog', async () => {
    open(`${ok}?tab=dead-letters`)
    const table = await screen.findByRole('table', { name: /Dead-letter messages of orders-sb/ })
    const row = within(table).getByRole('row', { name: /m-1/ })
    expect(row).toHaveTextContent('ProcessingError')
    expect(row).toHaveTextContent('Timed out')
    expect(within(row).getByText('In dead-letter')).toBeInTheDocument()
    expect(within(row).getByRole('link', { name: 'Open message m-1' })).toHaveAttribute('href', `${ok}?tab=dead-letters&dialog=message&id=1`)
  })

  it('says where a message stands from the pending-work read: Review for an approval, View for a hand-off', async () => {
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([approval({ dlqMessageId: 1, entryId: 'e1' }), handOff({ dlqMessageId: 2, entryId: 'e2' })]))
    open(`${ok}?tab=dead-letters`)
    const table = await screen.findByRole('table')
    expect(await within(table).findByText('Waiting for approval')).toBeInTheDocument()
    expect(within(table).getByText('Handed to a person')).toBeInTheDocument()
    expect(within(table).getByRole('link', { name: 'Review' })).toHaveAttribute('href', `${ok}?tab=dead-letters&dialog=review&entry=e1`)
    expect(within(table).getByRole('link', { name: 'View' })).toHaveAttribute('href', `${ok}?tab=dead-letters&panel=hand-off&entry=e2`)
  })

  it('a Viewer sees "Needs an Approver" in place of Review', async () => {
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me({ governanceActive: true, effectiveRole: 'Viewer', recoverRole: 'Viewer', grantors: ['maya'] }))
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([approval({ dlqMessageId: 1 })]))
    open(`${ok}?tab=dead-letters`)
    expect(await screen.findByText('Needs an Approver')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Review' })).not.toBeInTheDocument()
  })

  it('nothing replays from this list: a tick only chooses what Bulk replay will PREVIEW, and Replay selected waits for one', async () => {
    open(`${ok}?tab=dead-letters`)
    await screen.findByRole('table')
    expect(screen.getAllByRole('checkbox').length).toBeGreaterThan(0)
    expect(screen.getAllByRole('button', { name: 'Replay selected…' })[0]).toBeDisabled()
    expect(screen.getByRole('note')).toHaveTextContent('Nothing is replayed from this list')
  })

  it('filters live in the address: choosing a reason, an entity or a state asks again from page 1 and survives a refresh', async () => {
    const router = open(`${ok}?tab=dead-letters&page=3`)
    await screen.findByRole('table')
    await userEvent.click(screen.getByRole('button', { name: /ProcessingError/ }))
    await waitFor(() => expect(router.state.location.search).toContain('reason=ProcessingError'))
    expect(router.state.location.search).not.toContain('page=') // a new filter starts at the top
    await waitFor(() => expect(lastQuery()).toMatchObject({ reason: 'ProcessingError', page: 1 }))

    await userEvent.selectOptions(screen.getByLabelText('Entity'), 'orders-queue')
    await waitFor(() => expect(lastQuery()).toMatchObject({ entity: 'orders-queue' }))
    await userEvent.selectOptions(screen.getByLabelText('State'), 'all')
    await waitFor(() => expect(lastQuery()).toMatchObject({ status: 'all' }))
    expect(router.state.location.search).toContain('tab=dead-letters') // the tab is kept
  })

  it('opens already filtered when the address says so (the Entity View links), and Clear filters undoes it', async () => {
    const router = open(`${ok}?tab=dead-letters&entity=orders-queue&status=resolved&reason=ProcessingError&q=abc`)
    await screen.findByRole('table')
    expect(lastQuery()).toMatchObject({ entity: 'orders-queue', status: 'resolved', reason: 'ProcessingError', q: 'abc' })
    await userEvent.click(screen.getByRole('button', { name: 'Clear filters' }))
    await waitFor(() => expect(router.state.location.search).toBe('?tab=dead-letters'))
  })

  it('"no reason recorded" is its own filter and is said plainly, never hidden', async () => {
    vi.mocked(deadLettersApi.fetchDeadLetters).mockResolvedValue(result([letter(1, { deadLetterReason: null })], 1, { groups: [{ reason: null, count: 1 }] }))
    open(`${ok}?tab=dead-letters`)
    await userEvent.click(await screen.findByRole('button', { name: /No reason recorded/ }))
    await waitFor(() => expect(lastQuery()).toMatchObject({ noReason: true }))
  })

  it('search is applied a moment after typing, not on every key', async () => {
    const router = open(`${ok}?tab=dead-letters`)
    await screen.findByRole('table')
    const calls = vi.mocked(deadLettersApi.fetchDeadLetters).mock.calls.length
    await userEvent.type(screen.getByLabelText('Search messages'), 'gateway')
    expect(vi.mocked(deadLettersApi.fetchDeadLetters).mock.calls.length).toBe(calls) // nothing asked yet
    await waitFor(() => expect(router.state.location.search).toContain('q=gateway'), { timeout: 2000 })
  })

  it('pages on the server: the pager asks for page 2 and says "of n"', async () => {
    vi.mocked(deadLettersApi.fetchDeadLetters).mockResolvedValue(result(Array.from({ length: 25 }, (_, i) => letter(i + 1)), 60))
    const router = open(`${ok}?tab=dead-letters`)
    expect(await screen.findByText('1–25 of 60')).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Pages of dead-letter messages' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Next page' }))
    await waitFor(() => expect(router.state.location.search).toContain('page=2'))
    await waitFor(() => expect(lastQuery()).toMatchObject({ page: 2, pageSize: 25 }))
  })

  it('an unwatched connection says recorded, "showing x of n", and offers Look now', async () => {
    vi.mocked(deadLettersApi.fetchDeadLetters).mockResolvedValue(result([letter(1)], 200))
    open(`${ok}?tab=dead-letters`, component({ watched: false }), false)
    expect(await screen.findByRole('heading', { name: 'Recorded dead-letter messages (showing 1 of 200)' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Look now' })).toBeInTheDocument()
    expect(screen.getAllByRole('note').some((n) => /what ServiceHub recorded when you last looked/.test(n.textContent ?? ''))).toBe(true)
    expect(within(screen.getByRole('table')).getByText('Recorded', { selector: 'td span' })).toBeInTheDocument()
  })

  it('empty states say which kind of empty it is', async () => {
    vi.mocked(deadLettersApi.fetchDeadLetters).mockResolvedValue(result([], 0, { groups: [], entities: [] }))
    const { unmount } = (() => { const r = open(`${ok}?tab=dead-letters`); return { unmount: () => r.dispose() } })()
    expect(await screen.findByText('No message is in a dead-letter queue.')).toBeInTheDocument()
    unmount()
    document.body.innerHTML = ''
    open(`${ok}?tab=dead-letters&reason=Nope`)
    expect(await screen.findByText('Nothing matches these filters.')).toBeInTheDocument()
  })

  it('says it could not read, rather than showing an empty list that looks like good news', async () => {
    vi.mocked(deadLettersApi.fetchDeadLetters).mockRejectedValue(new Error('down'))
    open(`${ok}?tab=dead-letters`)
    expect(await screen.findByText('The dead-letter messages could not be read.')).toBeInTheDocument()
    expect(screen.queryByText('No message is in a dead-letter queue.')).not.toBeInTheDocument()
  })

  it('has no accessibility violations', async () => {
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([approval({ dlqMessageId: 1 }), handOff({ dlqMessageId: 2 })]))
    const { container } = (() => { open(`${ok}?tab=dead-letters`); return { container: document.body } })()
    await screen.findByText('Waiting for approval')
    await expectNoAxeViolations(container)
  })
})
