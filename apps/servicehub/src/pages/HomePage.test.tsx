import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as api from '../lib/api/namespaces'
import { fetchRecoverySummary } from '../lib/api/recovery'
import { fetchDeadLetters, fetchDeadLetterTrend, type DeadLetter } from '../lib/api/deadLetters'
import { fetchFleet, type FleetOverview } from '../lib/api/fleet'
import { fetchRules } from '../lib/api/rules'
import { fetchAudit, fetchMe } from '../lib/api/identity'
import { fetchAgents } from '../lib/api/agents'
import { fetchPendingWork } from '../lib/api/pendingWork'
import type { CloudProvider, Entity, Namespace, NamespaceStats } from '../lib/api/namespaces'
import { AppLayout } from '../layouts/AppLayout'
import { HomePage } from './HomePage'

vi.mock('../lib/api/namespaces')
vi.mock('../lib/api/deadLetters')
vi.mock('../lib/api/recovery')
vi.mock('../lib/api/fleet')
vi.mock('../lib/api/rules')
vi.mock('../lib/api/identity')
vi.mock('../lib/api/agents')
vi.mock('../lib/api/pendingWork')

const emptyPage = { items: [], paging: { total: 0, page: 1, pageSize: 5 }, groups: [], otherReasons: null, entities: [] }
const noReplays = { window: '7d', total: 0, states: [], byProvider: [], stayedFixedRate: null, returnedConfidence: { exact: 0, heuristic: 0 }, replaysAccepted: 0 } as never
const emptyFleet: FleetOverview = { window: '24h', since: 'x', clouds: [], namespaces: [], topFailures: [] }
const mocked = vi.mocked(api)

// The real ProviderCapabilities presets (services/api .../ProviderCapabilities.cs), so a test that says
// "gcp" gets Pub/Sub's actual shape (no counts) rather than an Azure-shaped namespace with a GCP label.
const ns = (id: string, provider: CloudProvider, over: Partial<Namespace> = {}): Namespace => ({
  id, name: id, displayName: id, provider, environment: 'dev', lastConnectionTestSucceeded: true, awsRegion: null, gcpProjectId: null,
  capabilities: {
    supportsMessageCounts: provider !== 'gcp', supportsManualDeadLetter: provider !== 'gcp', supportsPurge: provider !== 'azure', supportsScheduledMessages: provider === 'azure',
    supportsRepeatablePeek: provider === 'azure', supportsRecoveryMarker: true, canProveDlqAbsence: provider === 'azure',
    supportsTopics: true, supportsSubscriptions: true, notes: '',
  },
  ...over,
} as Namespace)

const stats = (namespaceId: string, over: Partial<NamespaceStats> = {}): NamespaceStats => ({
  namespaceId, entities: [{ kind: 'queue', count: 2 }], activeMessages: 10, deadLetterMessages: 4, messageCountsSupported: true, observedAt: 'now', ...over,
})
const entity = (dl: number | null): Entity => ({ name: 'q', kind: 'queue', activeMessages: 0, deadLetterMessages: dl, deadLetterTargetName: null })

function renderHome(url = '/') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[url]}>
        <Routes>
          <Route element={<AppLayout />}>
            <Route index element={<HomePage />} />
          </Route>
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('Home', () => {
  beforeEach(() => {
    // resetAllMocks (not clearAllMocks): a `mockRejectedValueOnce` queued in one test and never
    // actually consumed there (an unmounted query, a component that never re-fetched) would otherwise
    // leak into the next test's first call and silently break its render.
    vi.resetAllMocks()
    window.localStorage.clear()
    vi.mocked(fetchDeadLetters).mockResolvedValue(emptyPage)
    vi.mocked(fetchDeadLetterTrend).mockResolvedValue({ days: 7, series: [] })
    vi.mocked(fetchRecoverySummary).mockResolvedValue(noReplays)
    vi.mocked(fetchFleet).mockResolvedValue(emptyFleet)
    vi.mocked(fetchRules).mockResolvedValue([])
    vi.mocked(fetchAudit).mockResolvedValue({ items: [], page: 1, pageSize: 5, total: 0 })
    vi.mocked(fetchMe).mockResolvedValue({
      ownerId: 'owner', authMethod: 'session', governanceActive: false, effectiveRole: 'Admin',
      actor: { identity: 'session', kind: 'user', label: 'This browser session', isSession: true },
    })
    vi.mocked(fetchAgents).mockResolvedValue([])
    vi.mocked(fetchPendingWork).mockResolvedValue({ items: [], total: 0, byProvider: [], agents: 0 })
    mocked.fetchEntities.mockImplementation(async (id) => ({ namespaceId: id, entities: [entity(3), entity(0)] }))
  })

  describe('one cloud — straight to its own detail, no scope tabs', () => {
    it('shows the cloud, its numbers, and no scope control at all', async () => {
      mocked.fetchNamespaces.mockResolvedValue([ns('orders-dev', 'azure')])
      mocked.fetchNamespaceStats.mockResolvedValue(stats('orders-dev'))
      renderHome()

      expect(await screen.findByRole('heading', { name: 'Home' })).toBeInTheDocument()
      expect(await screen.findByRole('heading', { name: 'Azure' })).toBeInTheDocument()
      expect(screen.getByText('Dead letters', { selector: 'div' })).toBeInTheDocument()
      expect(screen.getByText('Active messages', { selector: 'div' })).toBeInTheDocument()
      expect(screen.getByText('Connected')).toBeInTheDocument()
      // Nothing to switch between with one cloud connected — no All-clouds tab, no cloud tab.
      expect(screen.queryByText('All clouds')).not.toBeInTheDocument()
    })

    it('adds only this cloud’s namespaces together', async () => {
      mocked.fetchNamespaces.mockResolvedValue([ns('a1', 'azure'), ns('a2', 'azure')])
      mocked.fetchNamespaceStats.mockImplementation(async (id) => stats(id, { deadLetterMessages: 4 }))
      renderHome()

      expect(await screen.findByText(/messages are/)).toHaveTextContent('8 messages are dead-lettered in Azure')
    })

    it('narrows to one namespace with ?ns=', async () => {
      mocked.fetchNamespaces.mockResolvedValue([ns('a1', 'azure'), ns('a2', 'azure')])
      mocked.fetchNamespaceStats.mockImplementation(async (id) => stats(id, { deadLetterMessages: id === 'a2' ? 5 : 4 }))
      renderHome('/?ns=a2')

      expect(await screen.findByText(/messages are/)).toHaveTextContent('5 messages are dead-lettered in Azure')
    })

    it('narrows to one environment with ?env=', async () => {
      mocked.fetchNamespaces.mockResolvedValue([ns('a1', 'azure', { environment: 'prod' }), ns('a2', 'azure', { environment: 'dev' }), ns('a3', 'azure', { environment: 'dev' })])
      mocked.fetchNamespaceStats.mockImplementation(async (id) => stats(id, { deadLetterMessages: 4 }))
      renderHome('/?env=dev')

      expect(await screen.findByText(/messages are/)).toHaveTextContent('8 messages are dead-lettered in Azure')
    })

    it('states the good-news case as a sentence', async () => {
      mocked.fetchNamespaces.mockResolvedValue([ns('orders-dev', 'azure')])
      mocked.fetchNamespaceStats.mockResolvedValue(stats('orders-dev', { deadLetterMessages: 0, entities: [{ kind: 'queue', count: 12 }] }))
      mocked.fetchEntities.mockResolvedValue({ namespaceId: 'orders-dev', entities: [entity(0)] })
      renderHome()

      expect(await screen.findByText(/No dead letters in Azure\. ServiceHub can see 12 queues\./)).toBeInTheDocument()
    })

    it('never turns "cannot count" into a zero, on a cloud that cannot count', async () => {
      mocked.fetchNamespaces.mockResolvedValue([ns('p1', 'gcp', { gcpProjectId: 'my-proj-123' })])
      mocked.fetchNamespaceStats.mockResolvedValue(
        stats('p1', { deadLetterMessages: null, activeMessages: null, messageCountsSupported: false, entities: [{ kind: 'topic', count: 3 }] }),
      )
      mocked.fetchEntities.mockResolvedValue({ namespaceId: 'p1', entities: [entity(null)] })
      renderHome()

      expect(await screen.findByText(/can’t say how many dead letters/)).toBeInTheDocument()
      expect(screen.queryByText(/No dead letters/)).not.toBeInTheDocument()
      expect(screen.getByText('Project')).toBeInTheDocument()
    })

    it('does not claim “Connected” for a cloud whose last test failed', async () => {
      mocked.fetchNamespaces.mockResolvedValue([ns('a1', 'azure', { lastConnectionTestSucceeded: false })])
      mocked.fetchNamespaceStats.mockResolvedValue(stats('a1'))
      renderHome()

      expect(await screen.findByText('Could not connect at last check')).toBeInTheDocument()
    })

    it('previews the latest dead letters in five rows and links to the full view', async () => {
      mocked.fetchNamespaces.mockResolvedValue([ns('a1', 'azure')])
      mocked.fetchNamespaceStats.mockResolvedValue(stats('a1'))
      const item = (id: number): DeadLetter => ({
        id, namespaceId: 'a1', messageId: `m-${id}`, sequenceNumber: id, entityName: 'orders', entityType: 'queue', topicName: null,
        detectedAtUtc: '2026-09-24T10:12:00Z', enqueuedTimeUtc: '2026-09-24T09:00:00Z', deliveryCount: 5, sizeInBytes: 2048,
        deadLetterReason: 'TimedOut', deadLetterErrorDescription: null, status: 'active',
      })
      vi.mocked(fetchDeadLetters).mockResolvedValue({ ...emptyPage, items: [1, 2, 3, 4, 5].map(item), paging: { total: 128, page: 1, pageSize: 5 } })
      renderHome()

      const section = await screen.findByRole('region', { name: 'Latest dead letters' })
      expect(within(section).getAllByRole('row')).toHaveLength(6)
      expect(within(section).getByRole('link', { name: 'See all 128 →' })).toHaveAttribute('href', '/?tab=dlq')
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
  })

  describe('a cloud that is not watched (AWS, Google Cloud) shapes its own view', () => {
    it('shows a Look now bar and no trend, for AWS', async () => {
      mocked.fetchNamespaces.mockResolvedValue([ns('w1', 'aws', { awsRegion: 'us-east-1' })])
      mocked.fetchNamespaceStats.mockResolvedValue(stats('w1', { deadLetterMessages: 5 }))
      renderHome()

      expect(await screen.findByText(/doesn’t watch on its own/)).toBeInTheDocument()
      expect(screen.getByRole('button', { name: /Look now/ })).toBeInTheDocument()
      expect(screen.queryByRole('status', { name: 'Reading the trend…' })).not.toBeInTheDocument()
      expect(screen.getByText(/Can’t confirm fixes yet/)).toBeInTheDocument()
    })

    it('draws the same Why-failed card from data every cloud has', async () => {
      mocked.fetchNamespaces.mockResolvedValue([ns('w1', 'aws', { awsRegion: 'us-east-1' })])
      mocked.fetchNamespaceStats.mockResolvedValue(stats('w1', { deadLetterMessages: 5 }))
      vi.mocked(fetchDeadLetters).mockResolvedValue({ ...emptyPage, groups: [{ reason: null, count: 3 }, { reason: 'Timeout', count: 2 }] } as never)
      renderHome()

      const why = await screen.findByRole('region', { name: 'Why messages failed' })
      expect(within(why).getByText('No reason recorded')).toBeInTheDocument()
      expect(within(why).getByText('Timeout')).toBeInTheDocument()
    })

    it('shows topics/subscriptions and a recorded-dead-letters tile for Google Cloud, which can’t count', async () => {
      mocked.fetchNamespaces.mockResolvedValue([ns('p1', 'gcp', { gcpProjectId: 'proj-1' })])
      mocked.fetchNamespaceStats.mockResolvedValue(stats('p1', { deadLetterMessages: null, activeMessages: null, messageCountsSupported: false, entities: [{ kind: 'topic', count: 2 }, { kind: 'subscription', count: 3 }] }))
      mocked.fetchEntities.mockResolvedValue({ namespaceId: 'p1', entities: [] })
      vi.mocked(fetchDeadLetters).mockResolvedValue({ ...emptyPage, paging: { total: 7, page: 1, pageSize: 1 } } as never)
      renderHome()

      expect(await screen.findByText('Recorded dead letters')).toBeInTheDocument()
      expect(screen.getByText('Topics · subscriptions')).toBeInTheDocument()
      expect(screen.getByText('Subscriptions')).toBeInTheDocument()
    })
  })

  describe('two or more clouds — All clouds first, divided by namespace', () => {
    it('opens on All clouds, with a tab per connected cloud and no cross-cloud sum', async () => {
      mocked.fetchNamespaces.mockResolvedValue([ns('a1', 'azure'), ns('w1', 'aws', { awsRegion: 'us-east-1' })])
      mocked.fetchNamespaceStats.mockImplementation(async (id) => stats(id, { deadLetterMessages: id === 'a1' ? 625 : 325 }))
      renderHome()

      const tabs = within(await screen.findByRole('tablist', { name: 'Scope' }))
      expect(await tabs.findByRole('tab', { name: /Azure.*625/s })).toBeInTheDocument()
      expect(await tabs.findByRole('tab', { name: /AWS.*325/s })).toBeInTheDocument()
      // 950 (625+325) is a sum across clouds — never shown anywhere on the page.
      expect(screen.queryByText('950')).not.toBeInTheDocument()
    })

    it('a scope tab opens that cloud’s own detail, and the sidebar row does the same thing', async () => {
      mocked.fetchNamespaces.mockResolvedValue([ns('a1', 'azure'), ns('w1', 'aws', { awsRegion: 'us-east-1' })])
      mocked.fetchNamespaceStats.mockImplementation(async (id) => stats(id, id === 'w1' ? { deadLetterMessages: 17 } : {}))
      renderHome()

      const tabs = within(await screen.findByRole('tablist', { name: 'Scope' }))
      await userEvent.click(tabs.getByRole('tab', { name: /^AWS/ }))

      expect(await screen.findByRole('heading', { name: 'AWS' })).toBeInTheDocument()
      expect(await screen.findByText(/messages are/)).toHaveTextContent('17 messages are dead-lettered in AWS')
      expect(screen.getByText('Region')).toBeInTheDocument()
      expect(screen.getByText('us-east-1', { selector: 'b' })).toBeInTheDocument()
    })

    it('an unconnected or unknown ?provider= falls back to All clouds', async () => {
      mocked.fetchNamespaces.mockResolvedValue([ns('a1', 'azure'), ns('w1', 'aws')])
      mocked.fetchNamespaceStats.mockResolvedValue(stats('a1'))
      renderHome('/?provider=gcp')

      expect(await screen.findByText('All clouds')).toBeInTheDocument()
    })
  })
})
