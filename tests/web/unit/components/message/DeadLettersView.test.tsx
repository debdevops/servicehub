import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { choose } from '../../../support/choose'
import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as dl from '@/lib/api/deadLetters'
import type { DeadLetter, DeadLetterPage } from '@/lib/api/deadLetters'
import * as ns from '@/lib/api/namespaces'
import type { Namespace } from '@/lib/api/namespaces'
import { DeadLettersView } from '@/components/message/DeadLettersView'
import { expectNoAxeViolations } from '@tests/support/axe'
import { bulkSelection } from '@/lib/bulkSelection'

vi.mock('@/lib/api/deadLetters')
const fetchMock = vi.mocked(dl.fetchDeadLetters)
vi.mock('@/lib/api/namespaces', async (original) => ({ ...(await original<typeof ns>()), lookAtDeadLetters: vi.fn() }))
const lookMock = vi.mocked(ns.lookAtDeadLetters)

const row = (id: number, over: Partial<DeadLetter> = {}): DeadLetter => ({
  id, namespaceId: 'n1', messageId: `m-${id}`, sequenceNumber: id, entityName: 'orders', entityType: 'queue', topicName: null,
  detectedAtUtc: '2026-09-24T10:12:00Z', enqueuedTimeUtc: '2026-09-24T09:00:00Z', deliveryCount: 5, sizeInBytes: 12 * 1024,
  deadLetterReason: 'MaxDeliveryCountExceeded', deadLetterErrorDescription: 'Unexpected token', status: 'active', ...over,
})

const pageOf = (items: DeadLetter[], over: Partial<DeadLetterPage> = {}): DeadLetterPage => ({
  items, paging: { total: items.length, page: 1, pageSize: 25 },
  groups: [{ reason: 'MaxDeliveryCountExceeded', count: items.length }], otherReasons: null, entities: ['orders'], ...over,
})

const azure = { id: 'n1', name: 'orders-dev', displayName: 'Orders', provider: 'azure', capabilities: { supportsRepeatablePeek: true } } as unknown as Namespace
const aws = { id: 'w1', name: 'sqs', displayName: 'SQS', provider: 'aws', capabilities: { supportsRepeatablePeek: false } } as unknown as Namespace

function Where() {
  const { pathname, search } = useLocation()
  return <output data-testid="where">{pathname + search}</output>
}

function renderView(provider: 'azure' | 'aws' = 'azure', namespaces = [azure], url = '/?tab=dlq') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[url]}>
        <Where />
        <Routes>
          <Route path="/" element={<DeadLettersView provider={provider} namespaces={namespaces} />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

const where = () => screen.getByTestId('where').textContent
const lastQuery = () => fetchMock.mock.calls.at(-1)![0]

describe('the Dead letters view', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    window.localStorage.clear()
    fetchMock.mockResolvedValue(pageOf([row(1), row(2)]))
  })

  it('has no accessibility violations (6.6)', async () => {
    const { container } = renderView()
    await screen.findByRole('table')
    await expectNoAxeViolations(container)
  })

  it('lists the cloud’s dead letters as a real table, newest first, with a caption', async () => {
    renderView()

    const table = await screen.findByRole('table', { name: 'Dead-lettered messages, newest first' })
    expect(within(table).getAllByRole('columnheader').map((h) => h.textContent)).toEqual(
      ['', 'When', 'Queue or topic', 'Message', 'Failed because', 'Tries', 'Waiting', 'Size', 'Actions'],
    )
    expect(within(table).getAllByRole('row')).toHaveLength(3)
    expect(lastQuery()).toMatchObject({ provider: 'azure', status: 'active', pageSize: 10, page: 1 })
    expect(screen.getByRole('heading', { name: /Azure — Dead letters/ })).toBeInTheDocument()
  })

  it('shows the recorded reason as a fact, and says plainly when there is none', async () => {
    fetchMock.mockResolvedValue(pageOf([row(1, { deadLetterReason: null, deadLetterErrorDescription: null })], { groups: [{ reason: null, count: 1 }] }))
    renderView()

    const table = await screen.findByRole('table')
    expect(within(table).getByText('Reason not recorded')).toBeInTheDocument()
  })

  it('opens the explainer, and Got it puts it away for good', async () => {
    renderView()
    expect(await screen.findByText('Dead letter', { selector: 'dt' })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Got it' }))
    expect(screen.queryByText('Dead letter', { selector: 'dt' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'What am I looking at?' })).toBeInTheDocument()
  })

  it('filters by a reason chip, keeps every chip visible, and says the pager counts what matches', async () => {
    fetchMock.mockImplementation(async (q) =>
      q.reason === 'TimedOut'
        ? pageOf([row(3, { deadLetterReason: 'TimedOut' })], { groups: [{ reason: 'MaxDeliveryCountExceeded', count: 2 }, { reason: 'TimedOut', count: 1 }] })
        : pageOf([row(1), row(2), row(3, { deadLetterReason: 'TimedOut' })], { groups: [{ reason: 'MaxDeliveryCountExceeded', count: 2 }, { reason: 'TimedOut', count: 1 }] }),
    )
    renderView()

    await userEvent.click(await screen.findByRole('button', { name: /1 TimedOut/ }))

    await waitFor(() => expect(where()).toBe('/?tab=dlq&reason=TimedOut'))
    expect(await screen.findByText(/1–1 of 1 matching/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /2 MaxDeliveryCountExceeded/ })).toBeInTheDocument()
    expect(screen.getByText(/Showing 1 of 3/)).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'show all reasons' }))
    await waitFor(() => expect(where()).toBe('/?tab=dlq'))
  })

  it('pages through the API’s pages and keeps the page in the URL', async () => {
    fetchMock.mockResolvedValue(pageOf([row(1)], { paging: { total: 60, page: 1, pageSize: 25 } }))
    renderView()

    await screen.findByText('1–25 of 60')
    await userEvent.click(screen.getByRole('button', { name: 'Next page' }))

    await waitFor(() => expect(where()).toBe('/?tab=dlq&page=2'))
    await waitFor(() => expect(lastQuery()).toMatchObject({ page: 2 }))
  })

  it('a new filter goes back to the first page', async () => {
    fetchMock.mockResolvedValue(pageOf([row(1)], { paging: { total: 60, page: 3, pageSize: 25 } }))
    renderView('azure', [azure], '/?tab=dlq&page=3')

    await choose(await screen.findByLabelText('Time window'), '7d')

    await waitFor(() => expect(where()).toBe('/?tab=dlq&range=7d'))
    expect(lastQuery()).toMatchObject({ range: '7d', page: 1 })
  })

  it('searches after a pause, in the URL, and never offers a body search', async () => {
    renderView()
    const box = await screen.findByPlaceholderText('Search ID, queue, reason, error')

    await userEvent.type(box, 'billing')

    await waitFor(() => expect(where()).toBe('/?tab=dlq&q=billing'), { timeout: 2000 })
    expect(lastQuery()).toMatchObject({ q: 'billing' })
    expect(screen.queryByText(/body/i, { selector: 'option, label, button' })).not.toBeInTheDocument()
  })

  it('selects rows, raises the bulk bar, and its action goes to the preview — the table runs nothing', async () => {
    renderView()
    const first = await screen.findByRole('checkbox', { name: 'Select message m-1' })

    await userEvent.click(first)

    const bar = screen.getByRole('region', { name: 'Selected messages' })
    // The action bar sits ABOVE the table and sticks under the header, so choosing a row never means scrolling to find what to do with it.
    expect(bar.compareDocumentPosition(screen.getByRole('table')) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(bar.className).toContain('sticky')
    expect(within(bar).getByText('1 message selected')).toBeInTheDocument()
    expect(within(bar).getByRole('link', { name: 'Replay selected…' })).toHaveAttribute('href', '/?tab=dlq&modal=bulk-replay')
    expect(within(bar).getByText(/preview first/)).toBeInTheDocument()
  })

  it('selects a whole page from the header, and every match from the bar when a reason is chosen', async () => {
    fetchMock.mockResolvedValue(pageOf([row(1), row(2)], { paging: { total: 47, page: 1, pageSize: 25 }, groups: [{ reason: 'Validation', count: 47 }] }))
    renderView('azure', [azure], '/?tab=dlq&reason=Validation')

    await userEvent.click(await screen.findByRole('checkbox', { name: 'Select all on this page' }))
    expect(screen.getAllByText('2 messages selected')).toHaveLength(2) // the bar above the table and the one below

    await userEvent.click(screen.getAllByRole('button', { name: 'Select all 47 Validation' })[0]!)
    expect(screen.getAllByText('47 messages selected')).toHaveLength(2)
  })

  it('drops the selection when the filter changes — a selection you cannot see is a hazard', async () => {
    renderView()
    await userEvent.click(await screen.findByRole('checkbox', { name: 'Select message m-1' }))
    expect(screen.getAllByRole('link', { name: 'Replay selected…' })).toHaveLength(2)

    await choose(screen.getByLabelText('Time window'), '24h')

    // The bar stays, but with nothing ticked Replay selected is a disabled button, not a link.
    await waitFor(() => expect(screen.queryByRole('link', { name: 'Replay selected…' })).not.toBeInTheDocument())
    expect(screen.getAllByRole('button', { name: 'Replay selected…' })[0]).toBeDisabled()
  })

  it('shows Replay selected disabled — top and bottom — until something is ticked, and frees it again once a bulk run has ended', async () => {
    renderView()
    await screen.findByRole('checkbox', { name: 'Select message m-1' })
    const bars = screen.getAllByRole('button', { name: 'Replay selected…' })
    expect(bars).toHaveLength(2)
    bars.forEach((b) => expect(b).toBeDisabled())

    await userEvent.click(screen.getByRole('checkbox', { name: 'Select message m-1' }))
    expect(screen.getAllByRole('link', { name: 'Replay selected…' })).toHaveLength(2)

    act(() => bulkSelection.finished()) // the run that carried the selection has ended
    await waitFor(() => expect(screen.getAllByRole('button', { name: 'Replay selected…' })).toHaveLength(2))
    expect(screen.getByRole('checkbox', { name: 'Select message m-1' })).not.toBeChecked()
  })

  it('links each row to its message, keeping the filters', async () => {
    renderView('azure', [azure], '/?tab=dlq&range=7d')
    const link = await screen.findByRole('link', { name: 'Details of message m-2' })
    expect(link).toHaveAttribute('href', '/?tab=dlq&range=7d&message=2')
  })

  it('says a filter matched nothing, and how to undo it — and says good news only when it is true', async () => {
    fetchMock.mockResolvedValue(pageOf([], { groups: [] }))
    const filtered = renderView('azure', [azure], '/?tab=dlq&q=zzz')
    expect(await screen.findByText('Nothing matches these filters.')).toBeInTheDocument()
    filtered.unmount()

    renderView('azure', [azure], '/?tab=dlq')
    expect(await screen.findByText(/No dead letters in Azure that ServiceHub has seen/)).toBeInTheDocument()
  })

  it('does not present silence as good news where ServiceHub does not watch the cloud', async () => {
    fetchMock.mockResolvedValue(pageOf([], { groups: [] }))
    renderView('aws', [aws])

    expect(await screen.findByText(/doesn’t look in AWS on its own/)).toBeInTheDocument()
    expect(await screen.findByText(/That does not mean there are no dead letters/)).toBeInTheDocument()
    expect(screen.queryByText(/No dead letters in AWS/)).not.toBeInTheDocument()
  })

  it('never looks in a cloud without a repeatable peek until a person asks, and says what the look cost', async () => {
    fetchMock.mockResolvedValue(pageOf([], { groups: [] }))
    lookMock.mockResolvedValue({ outcome: 'looked', queuesExamined: 2, newMessages: 7, resolved: 0, unconfirmed: 0, countsAsDeliveryAttempt: true, reason: null, lookedAtUtc: '2026-09-26T10:00:00Z' })
    renderView('aws', [aws])

    expect(await screen.findByText(/counts as one delivery attempt/)).toBeInTheDocument()
    expect(lookMock).not.toHaveBeenCalled()
    const calls = fetchMock.mock.calls.length

    await userEvent.click(screen.getByRole('button', { name: /Look at AWS’s dead letters now/ }))

    expect(lookMock).toHaveBeenCalledWith('w1')
    expect(await screen.findByText(/2 queues with dead letters: 7 new dead letters recorded/)).toBeInTheDocument()
    await waitFor(() => expect(fetchMock.mock.calls.length).toBeGreaterThan(calls)) // the list reads again
  })

  it('says a look hands back a sample, and never calls what it did not see gone', async () => {
    fetchMock.mockResolvedValue(pageOf([], { groups: [] }))
    lookMock.mockResolvedValue({ outcome: 'looked', queuesExamined: 1, newMessages: 3, resolved: 0, unconfirmed: 1, countsAsDeliveryAttempt: true, reason: null, lookedAtUtc: '2026-09-26T10:00:00Z' })
    renderView('aws', [aws])

    await userEvent.click(await screen.findByRole('button', { name: /Look at AWS’s dead letters now/ }))

    expect(await screen.findByText(/AWS hands back a sample, not the whole queue/)).toBeInTheDocument()
    expect(screen.queryByText(/no longer in the queue/)).not.toBeInTheDocument()
  })

  it('offers no look where ServiceHub already watches the cloud', async () => {
    renderView()
    await screen.findByRole('table')
    expect(screen.queryByRole('button', { name: /dead letters now/ })).not.toBeInTheDocument()
  })

  it('names the namespace beside the queue only when a cloud has more than one', async () => {
    const second = { ...azure, id: 'n2', displayName: 'Billing' } as Namespace
    fetchMock.mockResolvedValue(pageOf([row(1, { namespaceId: 'n2' })]))
    renderView('azure', [azure, second])

    expect(await screen.findByText('Billing')).toBeInTheDocument()
  })

  it('says a failure in words and offers to try again', async () => {
    fetchMock.mockRejectedValueOnce(new Error('AxiosError 500'))
    renderView()

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('couldn’t read its list of dead letters')
    expect(alert).not.toHaveTextContent(/500|Axios/)
    fetchMock.mockResolvedValue(pageOf([row(1)]))
    await userEvent.click(within(alert).getByRole('button', { name: 'Try again' }))
    expect(await screen.findByRole('table')).toBeInTheDocument()
  })

  it('shows history as one filter: no longer stuck rows say when and how they left, as recorded, and cannot be selected', async () => {
    fetchMock.mockResolvedValue(pageOf([
      row(1, { status: 'resolved', resolvedAt: '2026-09-25T08:00:00Z', resolutionCause: 'replayedByServiceHub' }),
      row(2, { status: 'resolved', resolvedAt: '2026-09-25T09:00:00Z', resolutionCause: 'vanishedExternally' }),
    ]))
    renderView()
    await choose(await screen.findByLabelText('Showing'), 'resolved')

    await waitFor(() => expect(lastQuery()).toMatchObject({ status: 'resolved' }))
    expect(where()).toContain('status=resolved')
    const table = await screen.findByRole('table', { name: /what became of them/ })
    expect(within(table).getAllByRole('columnheader').map((h) => h.textContent)).toEqual(
      ['When', 'Queue or topic', 'Message', 'Failed because', 'Tries', 'Now', 'Size', 'Actions'],
    )
    expect(within(table).getByText('Replayed by ServiceHub')).toBeInTheDocument()
    // Absence proves it is gone, never who removed it.
    expect(within(table).getByText('Left the queue — ServiceHub did not see how')).toBeInTheDocument()
    expect(within(table).queryByRole('checkbox')).not.toBeInTheDocument()
  })

  it('stuck now is the default and is not written into the link', async () => {
    renderView('azure', [azure], '/?tab=dlq&status=all')
    await choose(await screen.findByLabelText('Showing'), 'active')
    await waitFor(() => expect(where()).not.toContain('status='))
    expect(lastQuery()).toMatchObject({ status: 'active' })
  })
})
