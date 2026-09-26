import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as api from '../lib/api/namespaces'
import { fetchRecoverySummary } from '../lib/api/recovery'
import { fetchDeadLetters, type DeadLetter } from '../lib/api/deadLetters'
import type { CloudProvider, Entity, Namespace, NamespaceStats } from '../lib/api/namespaces'
import { AppLayout } from '../layouts/AppLayout'
import { HomePage } from './HomePage'

vi.mock('../lib/api/namespaces')
vi.mock('../lib/api/deadLetters')
vi.mock('../lib/api/recovery')
const emptyPage = { items: [], paging: { total: 0, page: 1, pageSize: 5 }, groups: [], otherReasons: null, entities: [] }
const noReplays = { window: '7d', total: 0, states: [], byProvider: [], stayedFixedRate: null, returnedConfidence: { exact: 0, heuristic: 0 }, replaysAccepted: 0 } as never
const mocked = vi.mocked(api)

const ns = (id: string, provider: CloudProvider, over: Partial<Namespace> = {}): Namespace =>
  ({ id, name: id, displayName: id, provider, environment: 'dev', lastConnectionTestSucceeded: true, awsRegion: null, gcpProjectId: null, ...over }) as Namespace

const stats = (namespaceId: string, over: Partial<NamespaceStats> = {}): NamespaceStats => ({
  namespaceId, entities: [{ kind: 'queue', count: 2 }], activeMessages: 10, deadLetterMessages: 4, messageCountsSupported: true, observedAt: 'now', ...over,
})
const entity = (dl: number | null): Entity => ({ name: 'q', kind: 'queue', activeMessages: 0, deadLetterMessages: dl, deadLetterTargetName: null })

function renderHome(url = '/?tab=overview') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      {/* A bare "/" is where the landing rule sends two clouds to Fleet Overview; a deep link is left alone. */}
      <MemoryRouter initialEntries={[url]}>
        <Routes>
          <Route element={<AppLayout />}>
            <Route index element={<HomePage />} />
            <Route path="fleet" element={<p>fleet</p>} />
          </Route>
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('Home — one cloud, real numbers', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    window.localStorage.clear()
    vi.mocked(fetchDeadLetters).mockResolvedValue(emptyPage)
    vi.mocked(fetchRecoverySummary).mockResolvedValue(noReplays)
    mocked.fetchEntities.mockImplementation(async (id) => ({ namespaceId: id, entities: [entity(3), entity(0)] }))
  })

  it('titles the page for its cloud and shows the two tiles that have a source', async () => {
    mocked.fetchNamespaces.mockResolvedValue([ns('orders-dev', 'azure')])
    mocked.fetchNamespaceStats.mockResolvedValue(stats('orders-dev'))
    renderHome()

    expect(await screen.findByRole('heading', { name: 'Azure — Home' })).toBeInTheDocument()
    expect(await screen.findByText('Dead letters', { selector: 'div' })).toBeInTheDocument()
    expect(screen.getByText('Active messages', { selector: 'div' })).toBeInTheDocument()
    // No source yet → not drawn, and not drawn as 0.
    expect(screen.queryByText(/Replayed today/i)).not.toBeInTheDocument()
    expect(screen.queryByText(/Auto Replay/i, { selector: 'div' })).not.toBeInTheDocument()
    expect(screen.getByText('Connected')).toBeInTheDocument()
    expect(screen.getByText('orders-dev', { selector: 'b' })).toBeInTheDocument()
  })

  it('adds only the chosen cloud’s namespaces together', async () => {
    mocked.fetchNamespaces.mockResolvedValue([ns('a1', 'azure'), ns('a2', 'azure'), ns('w1', 'aws', { awsRegion: 'us-east-1' })])
    mocked.fetchNamespaceStats.mockImplementation(async (id) =>
      stats(id, id === 'w1' ? { deadLetterMessages: 1000 } : { deadLetterMessages: 4 }),
    )
    renderHome()

    await screen.findByRole('heading', { name: 'Azure — Home' })
    expect(await screen.findByText(/messages are/)).toHaveTextContent('8 messages are dead-lettered in Azure')
    expect(screen.queryByText('1,008')).not.toBeInTheDocument()
  })

  it('draws the same two insight cards on a cloud with no trend, from data every cloud has', async () => {
    mocked.fetchNamespaces.mockResolvedValue([ns('w1', 'aws', { awsRegion: 'us-east-1' })])
    mocked.fetchNamespaceStats.mockResolvedValue(stats('w1', { deadLetterMessages: 5 }))
    vi.mocked(fetchDeadLetters).mockResolvedValue({ ...emptyPage, groups: [{ reason: null, count: 3 }, { reason: 'Timeout', count: 2 }] } as never)
    vi.mocked(fetchRecoverySummary).mockResolvedValue({
      window: '7d', total: 4, stayedFixedRate: 0.5, returnedConfidence: { exact: 1, heuristic: 0 }, replaysAccepted: 4, byProvider: [],
      states: [{ state: 'Recovered', count: 1 }, { state: 'Returned', count: 1 }, { state: 'Unverified', count: 2 }],
    } as never)
    renderHome('/?tab=overview')

    const why = await screen.findByRole('region', { name: 'Why messages failed' })
    expect(within(why).getByText('No reason recorded')).toBeInTheDocument()
    expect(within(why).getByText('Timeout')).toBeInTheDocument()
    const ended = screen.getByRole('region', { name: 'How replays ended' })
    expect(await within(ended).findByText('50%')).toBeInTheDocument()
    expect(within(ended).getByText(/can’t prove the queue stayed empty/)).toBeInTheDocument()
    expect(screen.getByText(/no day-by-day trend/)).toBeInTheDocument()
  })

  it('charts queue depth on one shared scale from counts alone, and says so where the cloud cannot count', async () => {
    mocked.fetchNamespaces.mockResolvedValue([ns('w1', 'aws')])
    mocked.fetchNamespaceStats.mockResolvedValue(stats('w1'))
    mocked.fetchEntities.mockResolvedValue({
      namespaceId: 'w1',
      entities: [
        { name: 'orders', kind: 'queue', activeMessages: 40, deadLetterMessages: 30, deadLetterTargetName: null },
        { name: 'idle', kind: 'queue', activeMessages: 0, deadLetterMessages: 0, deadLetterTargetName: null },
        { name: 'events', kind: 'topic', activeMessages: null, deadLetterMessages: null, deadLetterTargetName: null },
      ],
    })
    renderHome('/?tab=overview')

    const card = await screen.findByRole('region', { name: 'Queue depth' })
    expect(within(card).getByText('orders')).toBeInTheDocument()
    expect(within(card).queryByText('idle')).not.toBeInTheDocument()
    expect(within(card).queryByText('events')).not.toBeInTheDocument()
    expect(within(card).getByRole('link')).toHaveAttribute('href', '/?tab=dlq&entity=orders&ns=w1')
  })

  it('names each queue with its namespace once several are in scope, so equal names are told apart', async () => {
    mocked.fetchNamespaces.mockResolvedValue([ns('a1', 'azure'), ns('a2', 'azure', { displayName: 'Azure Two' })])
    mocked.fetchNamespaceStats.mockImplementation(async (id) => stats(id))
    mocked.fetchEntities.mockImplementation(async (id) => ({ namespaceId: id, entities: [{ name: 'orders', kind: 'queue', activeMessages: 5, deadLetterMessages: 1, deadLetterTargetName: null }] }))
    renderHome('/?tab=overview')

    const card = await screen.findByRole('region', { name: 'Queue depth' })
    expect(within(card).getByText('a1 · Development / orders')).toBeInTheDocument()
    expect(within(card).getByText('Azure Two · Development / orders')).toBeInTheDocument()
  })

  it('says so, in words, when nothing has been replayed or failed', async () => {
    mocked.fetchNamespaces.mockResolvedValue([ns('a1', 'azure')])
    mocked.fetchNamespaceStats.mockResolvedValue(stats('a1'))
    renderHome('/?tab=overview')

    expect(await screen.findByText(/Nothing has been replayed in Azure/)).toBeInTheDocument()
    expect(screen.getByText(/nothing to explain/)).toBeInTheDocument()
  })

  it('narrows Home to one namespace with ?ns= and offers the choice only when there are several', async () => {
    mocked.fetchNamespaces.mockResolvedValue([ns('a1', 'azure'), ns('a2', 'azure'), ns('w1', 'aws', { awsRegion: 'us-east-1' })])
    mocked.fetchNamespaceStats.mockImplementation(async (id) => stats(id, { deadLetterMessages: id === 'a2' ? 5 : 4 }))
    renderHome('/?tab=overview&ns=a2')

    expect(await screen.findByText(/messages are/)).toHaveTextContent('5 messages are dead-lettered in Azure')
    await userEvent.click(screen.getByRole('button', { name: 'Namespace' }))
    expect(screen.getByRole('option', { name: /a2/ })).toHaveAttribute('aria-selected', 'true')

    await userEvent.click(screen.getByRole('option', { name: /All namespaces/ }))
    expect(await screen.findByText(/messages are/)).toHaveTextContent('9 messages are dead-lettered in Azure')
  })

  it('narrows Home to one environment with ?env=, grouped in the picker', async () => {
    mocked.fetchNamespaces.mockResolvedValue([ns('a1', 'azure', { environment: 'prod' }), ns('a2', 'azure', { environment: 'dev' }), ns('a3', 'azure', { environment: 'dev' })])
    mocked.fetchNamespaceStats.mockImplementation(async (id) => stats(id, { deadLetterMessages: 4 }))
    renderHome('/?tab=overview&env=dev')

    expect(await screen.findByText(/messages are/)).toHaveTextContent('8 messages are dead-lettered in Azure')
    await userEvent.click(screen.getByRole('button', { name: 'Namespace' }))
    expect(screen.getByRole('group', { name: 'Production' })).toBeInTheDocument()
    expect(screen.getByRole('group', { name: 'Development' })).toBeInTheDocument()
  })

  it('ignores a ?ns= that belongs to another cloud', async () => {
    mocked.fetchNamespaces.mockResolvedValue([ns('a1', 'azure'), ns('a2', 'azure'), ns('w1', 'aws')])
    mocked.fetchNamespaceStats.mockResolvedValue(stats('a1', { deadLetterMessages: 4 }))
    renderHome('/?tab=overview&ns=w1')

    expect(await screen.findByText(/messages are/)).toHaveTextContent('8 messages are dead-lettered in Azure')
  })

  it('re-scopes wholly when another cloud is chosen', async () => {
    mocked.fetchNamespaces.mockResolvedValue([ns('a1', 'azure'), ns('w1', 'aws', { awsRegion: 'us-east-1' })])
    mocked.fetchNamespaceStats.mockImplementation(async (id) => stats(id, id === 'w1' ? { deadLetterMessages: 17 } : {}))
    renderHome()

    await screen.findByRole('heading', { name: 'Azure — Home' })
    await userEvent.click(within(await screen.findByRole('list', { name: 'Connected clouds' })).getByRole('button', { name: /AWS/ }))

    expect(await screen.findByRole('heading', { name: 'AWS — Home' })).toBeInTheDocument()
    expect(await screen.findByText(/messages are/)).toHaveTextContent('17 messages are dead-lettered in AWS')
    expect(screen.getByText('Region')).toBeInTheDocument()
    expect(screen.getByText('us-east-1', { selector: 'b' })).toBeInTheDocument()
    expect(screen.queryByText(/Azure/, { selector: 'h1' })).not.toBeInTheDocument()
  })

  it('states the good-news case as a sentence', async () => {
    mocked.fetchNamespaces.mockResolvedValue([ns('orders-dev', 'azure')])
    mocked.fetchNamespaceStats.mockResolvedValue(stats('orders-dev', { deadLetterMessages: 0, entities: [{ kind: 'queue', count: 12 }] }))
    mocked.fetchEntities.mockResolvedValue({ namespaceId: 'orders-dev', entities: [entity(0)] })
    renderHome()

    expect(await screen.findByText(/No dead letters in Azure\. ServiceHub can see 12 queues\./)).toBeInTheDocument()
  })

  it('never turns "cannot count" into a zero', async () => {
    mocked.fetchNamespaces.mockResolvedValue([ns('p1', 'gcp', { gcpProjectId: 'my-proj-123' })])
    mocked.fetchNamespaceStats.mockResolvedValue(
      stats('p1', { deadLetterMessages: null, activeMessages: null, messageCountsSupported: false, entities: [{ kind: 'topic', count: 3 }] }),
    )
    mocked.fetchEntities.mockResolvedValue({ namespaceId: 'p1', entities: [entity(null)] })
    renderHome()

    expect(await screen.findAllByText('Google Cloud does not report message counts.')).toHaveLength(2)
    expect(screen.getByText(/can’t say how many dead letters/)).toBeInTheDocument()
    expect(screen.queryByText(/No dead letters/)).not.toBeInTheDocument()
    expect(screen.queryByText(/with dead letters/)).not.toBeInTheDocument()
    expect(screen.getByText('Project')).toBeInTheDocument()
  })

  it('offers Fleet Overview only when there is another cloud to compare with', async () => {
    mocked.fetchNamespaces.mockResolvedValue([ns('a1', 'azure')])
    mocked.fetchNamespaceStats.mockResolvedValue(stats('a1'))
    const one = renderHome()
    await screen.findByRole('heading', { name: 'Azure — Home' })
    expect(screen.queryByRole('link', { name: /Compare with your other clouds/ })).not.toBeInTheDocument()
    one.unmount()

    mocked.fetchNamespaces.mockResolvedValue([ns('a1', 'azure'), ns('w1', 'aws')])
    window.localStorage.setItem('servicehub.provider', 'azure')
    renderHome()
    expect(await screen.findByRole('link', { name: /Compare with your other clouds/ })).toHaveAttribute('href', '/fleet')
  })

  it('names the entity kinds and the dead-letter count in words', async () => {
    mocked.fetchNamespaces.mockResolvedValue([ns('a1', 'azure')])
    mocked.fetchNamespaceStats.mockResolvedValue(stats('a1', { entities: [{ kind: 'queue', count: 7 }, { kind: 'topic', count: 1 }] }))
    renderHome()

    const card = await screen.findByRole('region', { name: 'Azure at a glance' })
    expect(within(card).getByText('7 queues')).toBeInTheDocument()
    expect(within(card).getByText('1 topic')).toBeInTheDocument()
    expect(within(card).getByText('1 with dead letters')).toBeInTheDocument()
    expect(within(card).queryByText(/DLQ/)).not.toBeInTheDocument()
  })

  it('does not leave the page blank when the list of clouds cannot be loaded', async () => {
    mocked.fetchNamespaces.mockRejectedValueOnce(new Error('AxiosError 502'))
    renderHome()

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('couldn’t load your clouds')
    expect(alert).not.toHaveTextContent(/502|AxiosError/)

    mocked.fetchNamespaces.mockResolvedValue([ns('a1', 'azure')])
    mocked.fetchNamespaceStats.mockResolvedValue(stats('a1'))
    await userEvent.click(within(alert).getByRole('button', { name: 'Try again' }))
    await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument())
  })

  it('says plainly when the cloud cannot be read, and offers to try again', async () => {
    mocked.fetchNamespaces.mockResolvedValue([ns('a1', 'azure')])
    mocked.fetchNamespaceStats.mockRejectedValueOnce(new Error('AxiosError 502'))
    renderHome()

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('couldn’t read Azure just now')
    expect(alert).not.toHaveTextContent(/502|AxiosError/)

    mocked.fetchNamespaceStats.mockResolvedValue(stats('a1'))
    await userEvent.click(within(alert).getByRole('button', { name: 'Try again' }))
    await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument())
  })

  it('does not claim “Connected” for a cloud whose last test failed', async () => {
    mocked.fetchNamespaces.mockResolvedValue([ns('a1', 'azure', { lastConnectionTestSucceeded: false })])
    mocked.fetchNamespaceStats.mockResolvedValue(stats('a1'))
    renderHome()

    expect(await screen.findByText('Could not connect at last check', { selector: 'span.rounded-full' })).toBeInTheDocument()
  })

  it('previews the latest dead letters in five rows and links to the full view', async () => {
    mocked.fetchNamespaces.mockResolvedValue([ns('a1', 'azure')])
    mocked.fetchNamespaceStats.mockResolvedValue(stats('a1'))
    const item = (id: number): DeadLetter => ({
      id, namespaceId: 'a1', messageId: `m-${id}`, sequenceNumber: id, entityName: 'orders', entityType: 'queue', topicName: null,
      detectedAtUtc: '2026-09-24T10:12:00Z', enqueuedTimeUtc: '2026-09-24T09:00:00Z', deliveryCount: 5, sizeInBytes: 2048,
      deadLetterReason: 'TimedOut', deadLetterErrorDescription: null, status: 'active',
    })
    vi.mocked(fetchDeadLetters).mockResolvedValue({
      ...emptyPage, items: [1, 2, 3, 4, 5].map(item), paging: { total: 128, page: 1, pageSize: 5 },
    })
    renderHome()

    const section = await screen.findByRole('region', { name: 'Latest dead letters' })
    expect(within(section).getAllByRole('row')).toHaveLength(6)
    expect(within(section).getByRole('link', { name: 'See all 128 →' })).toHaveAttribute('href', '/?tab=dlq')
    expect(vi.mocked(fetchDeadLetters)).toHaveBeenCalledWith(expect.objectContaining({ provider: 'azure', pageSize: 5 }))
  })

  it('draws no preview when there is nothing to preview', async () => {
    mocked.fetchNamespaces.mockResolvedValue([ns('a1', 'azure')])
    mocked.fetchNamespaceStats.mockResolvedValue(stats('a1'))
    renderHome()

    await screen.findByRole('heading', { name: 'Azure — Home' })
    await screen.findByRole('region', { name: 'Azure at a glance' })
    expect(screen.queryByRole('region', { name: 'Latest dead letters' })).not.toBeInTheDocument()
  })
})
