import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
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
import { nextRoute } from '@/next/router'
import { expectNoAxeViolations } from '@tests/support/axe'
import { agent, component, coverageRows, entity, me, page } from './homeData'

vi.mock('@/lib/api/coverage', async (o) => ({ ...(await o<typeof coverageApi>()), fetchCoverage: vi.fn() }))
vi.mock('@/lib/api/components', async (o) => ({ ...(await o<typeof componentsApi>()), fetchComponent: vi.fn(), fetchComponents: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn(), fetchSettings: vi.fn() }))
vi.mock('@/lib/api/pendingWork', async (o) => ({ ...(await o<typeof pendingApi>()), fetchPendingWork: vi.fn() }))
vi.mock('@/lib/api/namespaces', async (o) => ({ ...(await o<typeof namespacesApi>()), fetchNamespace: vi.fn(), fetchEntities: vi.fn(), fetchNamespaces: vi.fn(), fetchDlqObserver: vi.fn(), fetchEntitySettings: vi.fn() }))

const at = '2026-10-18T09:00:00Z'
const ok = '/next/azure/c/service-bus/ns-azure?tab=configuration'
const settings = (channels: settingsApi.NotificationChannel[]) => ({ notifications: { bellAlwaysOn: true, serverChannel: null, channels }, security: { apiKeysConfigured: 0, credentialsEncryptedAtRest: true, keyFingerprint: 'x' }, emergencyStop: { active: false, by: null, at: null, reason: null } }) as settingsApi.Settings
const channel = (label: string, enabled = true) => ({ id: label, format: 'slack', label, enabled, createdAt: at, createdBy: 'x', lastDeliveredAt: null, lastError: null }) as unknown as settingsApi.NotificationChannel
const ns = (canProve: boolean) => ({
  id: 'ns-azure', name: 'orders-sb', displayName: null, provider: 'azure', environment: 'dev', authType: 'connectionString', awsRegion: null, gcpProjectId: null,
  capabilities: { supportsMessageCounts: true, supportsRepeatablePeek: canProve, countsAreApproximate: false, canProveDlqAbsence: canProve },
}) as unknown as namespacesApi.Namespace
const observer = (over: Partial<namespacesApi.DlqObserver> = {}): namespacesApi.DlqObserver => ({
  needed: true, enabled: true, live: true, observerReference: 'r', dlqEntityName: null, stalenessBoundMinutes: 30, lastCanarySentAt: null, lastConfirmedAt: '2026-10-18T08:00:00Z', status: 'live', needsReference: false, referenceHint: null, ...over,
})

function open(c = component(), canProve = true, url = ok) {
  vi.mocked(componentsApi.fetchComponent).mockResolvedValue(c)
  vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [c], generatedAtUtc: at })
  vi.mocked(namespacesApi.fetchNamespace).mockResolvedValue(ns(canProve))
  vi.mocked(namespacesApi.fetchEntities).mockResolvedValue({ namespaceId: 'ns-azure', entities: [] })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(<QueryClientProvider client={client}><RouterProvider router={createMemoryRouter([nextRoute], { initialEntries: [url] })} /></QueryClientProvider>)
}

describe('the Configuration tab (unit D6)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
    vi.mocked(settingsApi.fetchSettings).mockResolvedValue(settings([channel('#payments-alerts'), channel('#old', false)]))
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([]))
    vi.mocked(namespacesApi.fetchDlqObserver).mockResolvedValue(observer())
  })

  it('shows, for each place, the one cloud-side setting the API really returns: where its dead letters go', async () => {
    open(component({ entities: [entity({ name: 'orders-queue', deadLetterStore: 'orders-queue-dlq' }), entity({ name: 'sub-1', parent: 'payments-topic', kind: 'subscription', deadLetterStore: null })] }))
    const table = await screen.findByRole('table', { name: /Settings Azure holds for each place in orders-sb/ })
    expect(within(table).getByRole('row', { name: /orders-queue/ })).toHaveTextContent('orders-queue-dlq')
    expect(within(table).getByRole('row', { name: /payments-topic \/ sub-1/ })).toHaveTextContent('Its own dead-letter place, not a separate one')
  })

  it('draws no column of dashes for the limits: they are read per place, only when asked', async () => {
    open()
    const table = await screen.findByRole('table')
    for (const never of ['Max deliveries', 'Lock duration', 'Time to live', 'Sessions', 'Dead-letter on expiry']) {
      expect(within(table).queryByRole('columnheader', { name: never }), never).not.toBeInTheDocument()
    }
    expect(namespacesApi.fetchEntitySettings).not.toHaveBeenCalled()
    expect(screen.getByText(/Press Show limits on a place to read its limits/)).toBeInTheDocument()
  })

  it('Show limits reads one place from the cloud and shows exactly what came back', async () => {
    vi.mocked(namespacesApi.fetchEntitySettings).mockResolvedValue({ name: 'orders-queue', kind: 'queue', settings: [{ label: 'Lock duration', value: '30 seconds' }, { label: 'Maximum delivery count', value: '10' }] })
    open(component({ entities: [entity({ name: 'orders-queue' })] }))
    const row = await screen.findByRole('row', { name: /orders-queue/ })
    await userEvent.setup().click(within(row).getByRole('button', { name: 'Show limits' }))
    expect(await within(row).findByText('30 seconds')).toBeInTheDocument()
    expect(within(row).getByText('Maximum delivery count')).toBeInTheDocument()
    expect(namespacesApi.fetchEntitySettings).toHaveBeenCalledTimes(1)
    expect(namespacesApi.fetchEntitySettings).toHaveBeenCalledWith('ns-azure', 'orders-queue', 'queue')
  })

  it('a cloud that does not read them for this kind of place says so, rather than showing nothing', async () => {
    vi.mocked(namespacesApi.fetchEntitySettings).mockResolvedValue({ name: 'orders-queue', kind: 'queue', settings: null })
    open(component({ entities: [entity({ name: 'orders-queue' })] }))
    const row = await screen.findByRole('row', { name: /orders-queue/ })
    await userEvent.setup().click(within(row).getByRole('button', { name: 'Show limits' }))
    expect(await within(row).findByText('Not read for this kind of place')).toBeInTheDocument()
  })

  it('says it could not read them, and offers to try again', async () => {
    vi.mocked(namespacesApi.fetchEntitySettings).mockRejectedValue(new Error('down'))
    open(component({ entities: [entity({ name: 'orders-queue' })] }))
    const row = await screen.findByRole('row', { name: /orders-queue/ })
    await userEvent.setup().click(within(row).getByRole('button', { name: 'Show limits' }))
    expect(await within(row).findByRole('alert')).toHaveTextContent('Could not be read')
    expect(within(row).getByRole('button', { name: 'Try again' })).toBeInTheDocument()
  })

  it('says ServiceHub never changes what the cloud holds', async () => {
    open()
    expect(await screen.findByText(/ServiceHub never changes them\./)).toBeInTheDocument()
  })

  it('shows ServiceHub’s own settings: who it tells (every enabled channel), the environment, and how it looks', async () => {
    open()
    const own = await screen.findByRole('region', { name: 'ServiceHub’s own settings for this connection' })
    expect(await within(own).findByText('Every enabled channel (1): #payments-alerts')).toBeInTheDocument()
    expect(own).toHaveTextContent('Environmentdev')
    expect(own).toHaveTextContent('LooksAutomatically')
    expect(within(own).getByRole('link', { name: 'Change' })).toHaveAttribute('href', '/next/configuration')
  })

  it('says only the bell tells you when no channel is set up', async () => {
    vi.mocked(settingsApi.fetchSettings).mockResolvedValue(settings([]))
    open()
    expect(await screen.findByText('No channel is set up, so only the bell tells you')).toBeInTheDocument()
  })

  it('says it could not read the channels rather than guessing', async () => {
    vi.mocked(settingsApi.fetchSettings).mockRejectedValue(new Error('down'))
    open()
    expect(await screen.findByText('Could not be read')).toBeInTheDocument()
  })

  it('a cloud that can confirm fixes itself has no observer line', async () => {
    open(component(), true)
    await screen.findByRole('region', { name: 'ServiceHub’s own settings for this connection' })
    expect(screen.queryByText('Confirming fixes')).not.toBeInTheDocument()
    expect(namespacesApi.fetchDlqObserver).not.toHaveBeenCalled()
  })

  it('a cloud that cannot says where fix confirmation stands, from its own status', async () => {
    open(component({ watched: false }), false)
    expect(await screen.findByText(/Live, last confirmed/)).toBeInTheDocument()
    expect(namespacesApi.fetchDlqObserver).toHaveBeenCalledWith('ns-azure')
  })

  it('set up but not live is said plainly, never as healthy', async () => {
    vi.mocked(namespacesApi.fetchDlqObserver).mockResolvedValue(observer({ live: false, lastConfirmedAt: null }))
    open(component({ watched: false }), false)
    expect(await screen.findByText('Set up, but not live: never confirmed yet')).toBeInTheDocument()
  })

  it('claims nothing about trust being demoted when a setting changes: that is not built', async () => {
    open()
    await screen.findByRole('table')
    expect(document.body.textContent).not.toMatch(/earned trust goes back|demote/i)
  })

  it('pages the places 25 at a time', async () => {
    open(component({ entities: Array.from({ length: 30 }, (_, i) => entity({ name: `queue-${String(i).padStart(2, '0')}` })) }))
    expect(await screen.findByText('1–25 of 30')).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Pages of places' })).toBeInTheDocument()
  })

  it('an unreachable connection says its places cannot be listed', async () => {
    open(component({ reachable: false, entities: [] }))
    expect(await screen.findByText('Its places cannot be listed until it can be reached.')).toBeInTheDocument()
  })

  it('has no accessibility violations', async () => {
    open()
    await screen.findByRole('table')
    await expectNoAxeViolations(document.body)
  })
})
