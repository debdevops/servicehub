import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as dl from '../../lib/api/deadLetters'
import type { DeadLetter, DeadLetterPage } from '../../lib/api/deadLetters'
import type { Namespace } from '../../lib/api/namespaces'
import { DeadLettersView } from './DeadLettersView'

vi.mock('../../lib/api/deadLetters')
const fetchMock = vi.mocked(dl.fetchDeadLetters)

const row = (id: number, over: Partial<DeadLetter> = {}): DeadLetter => ({
  id, namespaceId: 'n1', messageId: `m-${id}`, sequenceNumber: id, entityName: 'orders', entityType: 'queue', topicName: null,
  detectedAtUtc: '2026-09-24T10:12:00Z', enqueuedTimeUtc: '2026-09-24T09:00:00Z', deliveryCount: 5, sizeInBytes: 12 * 1024,
  deadLetterReason: 'MaxDeliveryCountExceeded', deadLetterErrorDescription: 'Unexpected token', status: 'Active', ...over,
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

  it('lists the cloud’s dead letters as a real table, newest first, with a caption', async () => {
    renderView()

    const table = await screen.findByRole('table', { name: 'Dead-lettered messages, newest first' })
    expect(within(table).getAllByRole('columnheader').map((h) => h.textContent)).toEqual(
      ['', 'When', 'Queue or topic', 'Failed because', 'Tries', 'Waiting', 'Size', 'Details'],
    )
    expect(within(table).getAllByRole('row')).toHaveLength(3)
    expect(lastQuery()).toMatchObject({ provider: 'azure', status: 'active', pageSize: 25, page: 1 })
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

    await userEvent.selectOptions(await screen.findByLabelText('Window'), '7d')

    await waitFor(() => expect(where()).toBe('/?tab=dlq&range=7d'))
    expect(lastQuery()).toMatchObject({ range: '7d', page: 1 })
  })

  it('searches after a pause, in the URL, and never offers a body search', async () => {
    renderView()
    const box = await screen.findByPlaceholderText('Search by message ID, queue or reason')

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
    expect(screen.getByText('2 messages selected')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Select all 47 Validation' }))
    expect(screen.getByText('47 messages selected')).toBeInTheDocument()
  })

  it('drops the selection when the filter changes — a selection you cannot see is a hazard', async () => {
    renderView()
    await userEvent.click(await screen.findByRole('checkbox', { name: 'Select message m-1' }))
    expect(screen.getByRole('region', { name: 'Selected messages' })).toBeInTheDocument()

    await userEvent.selectOptions(screen.getByLabelText('Window'), '24h')

    await waitFor(() => expect(screen.queryByRole('region', { name: 'Selected messages' })).not.toBeInTheDocument())
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

    expect(await screen.findByText(/does not watch AWS for dead letters on its own/)).toBeInTheDocument()
    expect(await screen.findByText(/That does not mean there are no dead letters/)).toBeInTheDocument()
    expect(screen.queryByText(/No dead letters in AWS/)).not.toBeInTheDocument()
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
})
