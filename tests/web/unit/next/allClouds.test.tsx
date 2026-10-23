import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as agentsApi from '@/lib/api/agents'
import * as componentsApi from '@/lib/api/components'
import * as coverageApi from '@/lib/api/coverage'
import * as identityApi from '@/lib/api/identity'
import * as pendingApi from '@/lib/api/pendingWork'
import * as settingsApi from '@/lib/api/settings'
import { nextRoute } from '@/next/router'
import { expectNoAxeViolations } from '@tests/support/axe'
import { agent, approval, component, coverageRows, handOff, me, page } from './homeData'

vi.mock('@/lib/api/coverage', async (o) => ({ ...(await o<typeof coverageApi>()), fetchCoverage: vi.fn() }))
vi.mock('@/lib/api/components', async (o) => ({ ...(await o<typeof componentsApi>()), fetchComponents: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn() }))
vi.mock('@/lib/api/pendingWork', async (o) => ({ ...(await o<typeof pendingApi>()), fetchPendingWork: vi.fn() }))

const generatedAtUtc = '2026-10-18T10:00:00Z'

const azure = () => component()
const aws = () => component({
  namespaceId: 'ns-aws', componentKey: 'sqs', cloud: 'aws', name: 'prod-aps1', componentName: 'SQS', watched: false, class: 'r2ReplayableUnproven',
  deadLetters: { value: 8700, kind: 'approximate' }, recorded: { messages: 300, reachedLimit: false, atUtc: null }, needsYou: 4, approvals: 3, handOffs: 1,
})
const gcp = () => component({
  namespaceId: 'ns-gcp', componentKey: 'pubsub', cloud: 'gcp', name: 'servicehub-prod', componentName: 'Pub/Sub', watched: false, class: 'r2ReplayableUnproven',
  deadLetters: { value: null, kind: 'notReported' }, recorded: { messages: 137, reachedLimit: false, atUtc: null }, needsYou: 2, approvals: 1, handOffs: 1,
})

function open(components: ReturnType<typeof component>[], items = [approval()]) {
  vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components, generatedAtUtc })
  vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page(items))
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}><RouterProvider router={createMemoryRouter([nextRoute], { initialEntries: ['/next'] })} /></QueryClientProvider>)
}

describe('Home for all clouds (unit C2)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
  })

  it('asks for every cloud at once, not one call per cloud', async () => {
    open([azure()])
    await screen.findByRole('heading', { name: 'All clouds' })
    expect(componentsApi.fetchComponents).toHaveBeenCalledWith(undefined)
    expect(componentsApi.fetchComponents).not.toHaveBeenCalledWith('azure')
  })

  it('three clouds: one card each in its own terms, the sums of ServiceHub’s own items, and no dead-letter total', async () => {
    open([azure(), aws(), gcp()])
    expect(await screen.findByTestId('all-needs-you')).toHaveTextContent('7') // 1 + 4 + 2
    expect(screen.getByTestId('all-approvals')).toHaveTextContent('5') // 1 + 3 + 1
    expect(screen.getByTestId('all-connected')).toHaveTextContent('3 clouds')
    expect(screen.getByText('3 components connected')).toBeInTheDocument()
    expect(screen.getByTestId('all-dead-letter')).toHaveTextContent('Counted per cloud') // AWS approximate, GCP reports none

    expect(screen.getByTestId('first-azure')).toHaveTextContent('12')
    expect(screen.getByTestId('first-aws')).toHaveTextContent('8,700(approx)')
    expect(screen.getByTestId('first-gcp')).toHaveTextContent('137')
    expect(within(screen.getByRole('region', { name: 'Google Cloud' })).getByText('Dead letters recorded')).toBeInTheDocument()
    expect(screen.queryByText(/8,849|8,712|12 \+ /)).not.toBeInTheDocument() // 12 + 8,700 + 137 is never drawn

    expect(screen.getByRole('note')).toHaveTextContent('No total across clouds. Azure counts are exact, AWS counts are approximate, and Google Cloud reports none')
  })

  it('the dead-letter tile sums only when every cloud counts exactly', async () => {
    open([azure(), { ...azure(), namespaceId: 'ns-az2', cloud: 'azure' }])
    expect(await screen.findByTestId('all-dead-letter')).toHaveTextContent('24')
    expect(screen.getByText('Across 1 cloud · latest values read by ServiceHub')).toBeInTheDocument()
  })

  it('each card says how it is watched, whether it counts, and what a replay can reach — from its own data', async () => {
    open([azure(), aws(), gcp()])
    const azureCard = await screen.findByRole('region', { name: 'Azure' })
    expect(within(azureCard).getByText('Watched automatically')).toBeInTheDocument()
    expect(within(azureCard).getByText('Counts messages')).toBeInTheDocument()
    expect(within(azureCard).getByText('Can reach Verified')).toBeInTheDocument()

    const gcpCard = screen.getByRole('region', { name: 'Google Cloud' })
    expect(within(gcpCard).getByText('Recorded when you look')).toBeInTheDocument()
    expect(within(gcpCard).getByText('No message counts')).toBeInTheDocument()
    expect(within(gcpCard).getByText('Replays end Unverified')).toBeInTheDocument()
  })

  it('Open takes you to that cloud’s own Home', async () => {
    open([azure(), aws()])
    expect(await screen.findByRole('link', { name: 'Open AWS' })).toHaveAttribute('href', '/next/aws')
    expect(screen.getByRole('link', { name: 'Open Azure' })).toHaveAttribute('href', '/next/azure')
  })

  it('two clouds: only those two get a card, and the sentence names only them', async () => {
    open([azure(), aws()])
    await screen.findByRole('region', { name: 'Azure' })
    expect(screen.queryByRole('region', { name: 'Google Cloud' })).not.toBeInTheDocument()
    expect(screen.getByTestId('all-connected')).toHaveTextContent('2 clouds')
    expect(screen.getByRole('note')).toHaveTextContent('Azure counts are exact and AWS counts are approximate')
  })

  it('one cloud: no "no total" sentence, because there is nothing to add up', async () => {
    open([azure()])
    expect(await screen.findByTestId('all-connected')).toHaveTextContent('1 cloud')
    expect(screen.queryByRole('note')).not.toBeInTheDocument()
  })

  it('a cloud that reports no counts and has not been looked at says so, never 0', async () => {
    open([gcp(), { ...gcp(), namespaceId: 'ns-gcp2', recorded: { messages: null, reachedLimit: false, atUtc: null } }].slice(1))
    expect(await screen.findByTestId('first-gcp')).toHaveTextContent('Not looked yet')
  })

  it('a cloud that could not be read says "Can’t read", with the component marked Can’t reach', async () => {
    open([component({ reachable: false, lastError: 'denied', needsYou: 0, approvals: 0, deadLetters: { value: null, kind: 'notRead' }, observedAtUtc: null })], [])
    expect(await screen.findByTestId('first-azure')).toHaveTextContent('Can’t read')
    expect(within(screen.getByRole('region', { name: 'Azure' })).getByText('Can’t reach')).toBeInTheDocument()
  })

  it('nothing connected: says so, rather than three empty cards', async () => {
    open([], [])
    expect(await screen.findByText(/No cloud is connected yet/)).toBeInTheDocument()
    expect(screen.getByTestId('all-connected')).toHaveTextContent('0 clouds')
  })

  it('one attention table across clouds, with a Cloud column', async () => {
    open([azure(), aws()], [approval(), approval({ id: 'x', entryId: 'x', provider: 'aws', componentKey: 'sqs', namespaceName: 'prod-aps1' }), handOff({ provider: 'gcp' })])
    const table = await screen.findByRole('region', { name: /Items that need your attention/ })
    expect(await within(table).findByRole('columnheader', { name: 'Cloud' })).toBeInTheDocument()
    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows.map((r) => within(r).getAllByRole('cell')[0].textContent)).toEqual(['Azure', 'AWS', 'Google Cloud'])
  })

  it('has no accessibility violations with three clouds', async () => {
    const { container } = open([azure(), aws(), gcp()], [approval(), handOff({ provider: 'aws' })])
    await screen.findByRole('region', { name: 'Google Cloud' })
    await screen.findByText('Hand-offs (1)')
    await expectNoAxeViolations(container)
  })
})
