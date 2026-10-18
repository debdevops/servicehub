import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as api from '@/lib/api/coverage'
import type { CoverageRow } from '@/lib/api/coverage'
import { onRequestComponents, sidebarComponents } from '@/hooks/useCoverage'
import { cloudOf, parseScope } from '@/next/lib/clouds'
import { componentTabs, nextHref, nextPages } from '@/next/nav/navigation'
import { nextRoute } from '@/next/router'
import { expectNoAxeViolations } from '@tests/support/axe'

vi.mock('@/lib/api/coverage', async (original) => ({ ...(await original<typeof api>()), fetchCoverage: vi.fn() }))

const row = (over: Partial<CoverageRow>): CoverageRow => ({
  id: 'x.y', componentKey: 'x', cloud: 'azure', cloudName: 'Azure', name: 'X', stuckPlace: 'Dead-letter queue', detection: 'store',
  detectionWords: 'Reads the place that holds the failed message', movesOnBy: 'ServiceHub replays one message', status: 'supported',
  statusWords: 'Supported', note: null, class: 'r1ReplayableProvable', offersReplay: true, offersCloudRedrive: false, mayEarnAutonomy: true,
  inSidebar: true, entityKinds: ['queue'], planUnit: null, ...over,
})

const rows: CoverageRow[] = [
  row({ id: 'service-bus.dead-letter', componentKey: 'service-bus', name: 'Service Bus' }),
  row({ id: 'service-bus.stuck', componentKey: 'service-bus', name: 'Service Bus', stuckPlace: 'Deferred messages', status: 'beingValidated', statusWords: 'Being validated', inSidebar: false }),
  row({ id: 'storage-queues.poison', componentKey: 'storage-queues', name: 'Storage Queues', status: 'beingValidated', statusWords: 'Being validated', class: 'r0Unknown', offersReplay: false }),
  row({ id: 'event-grid.dead-letter-blobs', componentKey: 'event-grid', name: 'Event Grid', status: 'onRequest', statusWords: 'On request', inSidebar: false }),
  row({ id: 'azure.not-messaging', componentKey: 'azure-data', name: 'Cosmos DB', status: 'notSupported', statusWords: 'Not supported', inSidebar: false, note: 'No message, so nothing to put back' }),
  row({ id: 'sqs.dead-letter', componentKey: 'sqs', cloud: 'aws', cloudName: 'AWS', name: 'SQS', class: 'r2ReplayableUnproven', mayEarnAutonomy: false, note: 'Ends Unverified' }),
  row({ id: 'pubsub.dead-letter', componentKey: 'pubsub', cloud: 'gcp', cloudName: 'Google Cloud', name: 'Pub/Sub', class: 'r2ReplayableUnproven', mayEarnAutonomy: false }),
]

function open(url: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}><RouterProvider router={createMemoryRouter([nextRoute], { initialEntries: [url] })} /></QueryClientProvider>)
}

describe('the 5.0.0 shell', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(api.fetchCoverage).mockResolvedValue({ rows })
  })

  it('builds the menu from the coverage catalogue and the navigation array, and names nothing by hand', async () => {
    open('/next/azure')
    const menu = screen.getByRole('navigation', { name: 'ServiceHub' })

    expect(await within(menu).findByRole('link', { name: 'Service Bus' })).toHaveAttribute('href', '/next/azure/c/service-bus')
    // A component that is in 5.0.0 but has no recorded live run says so in the menu itself.
    expect(within(menu).getByRole('link', { name: /Storage Queues.*Not yet validated/ })).toBeInTheDocument()
    // Another cloud's component is not listed while Azure is the scope.
    expect(within(menu).queryByRole('link', { name: 'SQS' })).not.toBeInTheDocument()
    // "On request" is listed, and is not a link: nothing behind it exists.
    expect(within(menu).getByText('Event Grid')).toBeInTheDocument()
    expect(within(menu).queryByRole('link', { name: /Event Grid/ })).not.toBeInTheDocument()
    expect(within(menu).getByRole('link', { name: 'Needs your attention' })).toHaveAttribute('href', '/next/azure/attention')
    expect(within(menu).getByRole('link', { name: 'What ServiceHub covers' })).toHaveAttribute('href', '/next/coverage')
  })

  it('shows every cloud’s components when no single cloud is the scope', async () => {
    open('/next')
    const menu = screen.getByRole('navigation', { name: 'ServiceHub' })

    expect(await within(menu).findByRole('link', { name: 'SQS' })).toHaveAttribute('href', '/next/aws/c/sqs')
    expect(within(menu).getByRole('link', { name: 'Pub/Sub' })).toBeInTheDocument()
    expect(within(menu).getByRole('link', { name: 'All clouds' })).toHaveAttribute('aria-current', 'true')
  })

  it.each(nextPages.filter((p) => !p.built).map((p) => [p.id, p] as const))('%s says plainly that it is not built, and which unit and design it waits for', async (_id, page) => {
    open(nextHref(page, { cloud: 'azure', component: 'service-bus', namespaceId: 'ns-1' }))

    expect(await screen.findByRole('heading', { level: 1 })).toBeInTheDocument()
    expect(screen.getByText(new RegExp(`Not built yet — unit ${page.id === 'component-detail' ? componentTabs[0].unit : page.unit}`))).toBeInTheDocument()
  })

  it('answers an address that names no page with the not-found screen, inside the shell', async () => {
    open('/next/azure/c/service-bus/ns-1/extra/deeper')
    expect(await screen.findByRole('heading', { level: 1, name: /doesn’t exist/ })).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'ServiceHub' })).toBeInTheDocument()
  })
})

describe('What ServiceHub covers', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('shows one table per cloud with the server’s own words, and counts each status', async () => {
    vi.mocked(api.fetchCoverage).mockResolvedValue({ rows })
    const { container } = open('/next/coverage')

    const azure = await screen.findByRole('region', { name: 'Azure' })
    expect(within(azure).getByRole('rowheader', { name: 'Cosmos DB' })).toBeInTheDocument()
    expect(within(azure).getByText('No message, so nothing to put back')).toBeInTheDocument()
    expect(within(screen.getByRole('region', { name: 'AWS' })).getByText('Ends Unverified')).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Google Cloud' })).toBeInTheDocument()
    // 3 supported (Service Bus, SQS, Pub/Sub), 2 being validated, 1 on request, 1 not supported.
    const counts = screen.getAllByRole('definition').filter((d) => /^\d+$/.test(d.textContent ?? '')).map((d) => d.textContent)
    expect(counts).toEqual(['3', '2', '1', '1'])
    await expectNoAxeViolations(container)
  })

  it('says it is loading, then says in words when the list cannot be read — never an empty table', async () => {
    let fail: (reason: unknown) => void = () => {}
    vi.mocked(api.fetchCoverage).mockReturnValue(new Promise((_resolve, reject) => { fail = reject }))
    open('/next/coverage')

    expect(await screen.findByText('Loading what ServiceHub covers…')).toBeInTheDocument()
    fail(new Error('boom'))
    expect(await screen.findByRole('alert')).toHaveTextContent('ServiceHub could not load the coverage list.')
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })
})

describe('the menu’s two lists', () => {
  it('lists a component once however many places it has, and only for the cloud asked', () => {
    expect(sidebarComponents(rows, 'azure').map((r) => r.componentKey)).toEqual(['service-bus', 'storage-queues'])
    expect(sidebarComponents(rows, null).map((r) => r.componentKey)).toEqual(['service-bus', 'storage-queues', 'sqs', 'pubsub'])
    expect(onRequestComponents(rows, 'azure').map((r) => r.name)).toEqual(['Event Grid'])
    expect(onRequestComponents(rows, 'aws')).toEqual([])
  })

  it('reads the scope from the address and never guesses at a segment it does not know', () => {
    expect(parseScope('aws')).toBe('aws')
    expect(parseScope('coverage')).toBe('all')
    expect(parseScope(undefined)).toBe('all')
    expect(cloudOf('all')).toBeNull()
    expect(cloudOf('gcp')).toBe('gcp')
  })
})
