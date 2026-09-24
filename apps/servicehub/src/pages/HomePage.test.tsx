import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as api from '../lib/api/namespaces'
import { fetchDeadLetters, type DeadLetter } from '../lib/api/deadLetters'
import type { CloudProvider, Entity, Namespace, NamespaceStats } from '../lib/api/namespaces'
import { AppLayout } from '../layouts/AppLayout'
import { HomePage } from './HomePage'

vi.mock('../lib/api/namespaces')
vi.mock('../lib/api/deadLetters')
const emptyPage = { items: [], paging: { total: 0, page: 1, pageSize: 5 }, groups: [], otherReasons: null, entities: [] }
const mocked = vi.mocked(api)

const ns = (id: string, provider: CloudProvider, over: Partial<Namespace> = {}): Namespace =>
  ({ id, name: id, displayName: id, provider, lastConnectionTestSucceeded: true, awsRegion: null, gcpProjectId: null, ...over }) as Namespace

const stats = (namespaceId: string, over: Partial<NamespaceStats> = {}): NamespaceStats => ({
  namespaceId, entities: [{ kind: 'queue', count: 2 }], activeMessages: 10, deadLetterMessages: 4, messageCountsSupported: true, observedAt: 'now', ...over,
})
const entity = (dl: number | null): Entity => ({ name: 'q', kind: 'queue', activeMessages: 0, deadLetterMessages: dl, deadLetterTargetName: null })

function renderHome() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      {/* A bare "/" is where the landing rule sends two clouds to Fleet Overview; a deep link is left alone. */}
      <MemoryRouter initialEntries={['/?tab=overview']}>
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
      deadLetterReason: 'TimedOut', deadLetterErrorDescription: null, status: 'Active',
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
