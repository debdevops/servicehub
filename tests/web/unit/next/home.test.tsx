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
import * as signaturesApi from '@/lib/api/signatures'
import { nextRoute } from '@/next/router'
import { expectNoAxeViolations } from '@tests/support/axe'
import { agent, approval, component, coverageRows, entity, handOff, me, page } from './homeData'

vi.mock('@/lib/api/coverage', async (o) => ({ ...(await o<typeof coverageApi>()), fetchCoverage: vi.fn() }))
vi.mock('@/lib/api/components', async (o) => ({ ...(await o<typeof componentsApi>()), fetchComponents: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn() }))
vi.mock('@/lib/api/pendingWork', async (o) => ({ ...(await o<typeof pendingApi>()), fetchPendingWork: vi.fn() }))
vi.mock('@/lib/api/signatures', async (o) => ({ ...(await o<typeof signaturesApi>()), fetchSignatures: vi.fn(), fetchSignatureTrust: vi.fn() }))
vi.mock('@/lib/api/namespaces', async (o) => ({ ...(await o<typeof namespacesApi>()), lookAtDeadLetters: vi.fn() }))

const now = '2026-10-18T10:00:00Z'
const generatedAtUtc = now

function open(url: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}><RouterProvider router={createMemoryRouter([nextRoute], { initialEntries: [url] })} /></QueryClientProvider>)
}

const trust = (over: Partial<signaturesApi.SignatureTrust> = {}): signaturesApi.SignatureTrust => ({
  level: 'approve', sampleSize: 0, verifiedSuccessRate: null, recovered: 0, returned: 0, failed: 0, unverified: 0, nextLevel: null, moreVerifiedNeeded: null, rateNeeded: null,
  cloudCanConfirm: true, productionCeiling: false, reasons: [], ...over,
})
const signature = (over: Partial<signaturesApi.Signature> = {}): signaturesApi.Signature => ({
  signatureHash: 'h1', provider: 'azure', reason: 'MaxDeliveryExceeded', exampleError: null, entities: ['orders-queue'], messages: 6, activeNow: 6, firstSeenAt: now, lastSeenAt: now, daily: [],
  growing: false, replays: { replayed: 0, stayedFixed: 0, returned: 0, unverified: 0 }, replayVerdict: 'unknown',
  namespaces: [{ id: 'ns-azure', name: 'orders-sb', displayName: null, environment: 'dev', messages: 6 }], ...over,
})
const signaturePage = (items: signaturesApi.Signature[]) => ({ items, total: items.length, page: 1, pageSize: 4, all: items.length, growing: 0, replayHelps: 0, replayDoesNotHelp: 0 })

const awsRow = () => component({
  namespaceId: 'ns-aws', componentKey: 'sqs', cloud: 'aws', name: 'prod-aps1', componentName: 'SQS', watched: false, class: 'r2ReplayableUnproven',
  deadLetters: { value: 7840, kind: 'approximate' }, recorded: { messages: 200, reachedLimit: false, atUtc: null }, needsYou: 3, approvals: 2, handOffs: 1,
  entities: [entity({ name: 'orders-queue', deadLetterStore: 'orders-queue-dlq', deadLetters: { value: 6420, kind: 'approximate' }, recorded: { messages: 100, reachedLimit: true, atUtc: null }, needsYou: 3 })],
})
const gcpRow = () => component({
  namespaceId: 'ns-gcp', componentKey: 'pubsub', cloud: 'gcp', name: 'servicehub-prod', componentName: 'Pub/Sub', watched: false, class: 'r2ReplayableUnproven',
  deadLetters: { value: null, kind: 'notReported' }, recorded: { messages: 137, reachedLimit: false, atUtc: null }, needsYou: 2, approvals: 1, handOffs: 1,
  entities: [entity({ name: 'orders-sub', parent: 'orders-topic', kind: 'subscription', deadLetters: { value: null, kind: 'notReported' }, recorded: { messages: null, reachedLimit: false, atUtc: null } })],
})

describe('Home for one cloud (unit C1)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [component()], generatedAtUtc })
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([approval()]))
    vi.mocked(signaturesApi.fetchSignatures).mockResolvedValue(signaturePage([signature()]))
    vi.mocked(signaturesApi.fetchSignatureTrust).mockResolvedValue(trust())
  })

  it('asks for exactly its own cloud, from the two reads that feed every number', async () => {
    open('/next/azure')
    await screen.findByRole('heading', { name: 'Azure Dashboard' })
    // Queries start just after the first render, so wait for them rather than racing the heading.
    await waitFor(() => {
      expect(componentsApi.fetchComponents).toHaveBeenCalledWith('azure')
      expect(pendingApi.fetchPendingWork).toHaveBeenCalledWith({ provider: 'azure' })
      expect(signaturesApi.fetchSignatures).toHaveBeenCalledWith(expect.objectContaining({ provider: 'azure' }))
    })
  })

  it('a watched cloud with exact counts: the first tile is "In dead-letter now", there is no Recorded column and no Look now', async () => {
    open('/next/azure')
    expect(await screen.findByTestId('tile-first-value')).toHaveTextContent('12')
    expect(screen.getByRole('region', { name: 'In dead-letter now' })).toBeInTheDocument()
    expect(screen.getByTestId('tile-needs-you')).toHaveTextContent('1')
    expect(screen.getByTestId('tile-approvals')).toHaveTextContent('1')
    expect(screen.queryByRole('columnheader', { name: 'Recorded' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Look now' })).not.toBeInTheDocument()
    const row = within(screen.getByRole('region', { name: /Azure PaaS components/ })).getAllByRole('row')[1]
    expect(within(row).getByText('Connected')).toBeInTheDocument()
    expect(within(row).getByText('Needs you')).toBeInTheDocument()
    expect(within(row).getByText('Read + Send')).toBeInTheDocument()
  })

  it('an approximate count is labelled approximate, and a cloud that is not watched says so, with Look now and a Recorded column', async () => {
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [awsRow()], generatedAtUtc })
    open('/next/aws')
    expect(await screen.findByTestId('tile-first-value')).toHaveTextContent('7,840(approx)')
    expect(screen.getByText(/200 recorded at last look/)).toBeInTheDocument()
    expect(screen.getByRole('note')).toHaveTextContent('AWS is not watched in the background.')
    expect(screen.getByRole('button', { name: 'Look now' })).toBeEnabled()
    expect(screen.getByRole('columnheader', { name: 'Recorded' })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: 'Last look' })).toBeInTheDocument()
  })

  it('a cloud that reports no counts is "Dead letters recorded" and never shows 0 as a count', async () => {
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [gcpRow()], generatedAtUtc })
    open('/next/gcp')
    expect(await screen.findByRole('region', { name: 'Dead letters recorded' })).toBeInTheDocument()
    expect(screen.getByTestId('tile-first-value')).toHaveTextContent('137')
    const table = screen.getByRole('region', { name: /Google Cloud PaaS components/ })
    expect(within(table).getAllByText('Can’t count here').length).toBeGreaterThan(0)
  })

  it('says "Not looked yet" for an entity nobody has looked at, once the row is expanded', async () => {
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [gcpRow()], generatedAtUtc })
    open('/next/gcp')
    await userEvent.click(await screen.findByRole('button', { name: /Show the places in servicehub-prod Pub\/Sub/ }))
    const places = screen.getByRole('table', { name: 'Places in servicehub-prod Pub/Sub' })
    expect(within(places).getByText('orders-topic / orders-sub')).toBeInTheDocument()
    expect(within(places).getByText('Not looked yet')).toBeInTheDocument()
  })

  it('shows a dead-letter queue beside its source and "(limit)" when a look stopped early', async () => {
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [awsRow()], generatedAtUtc })
    open('/next/aws')
    await userEvent.click(await screen.findByRole('button', { name: /Show the places/ }))
    const places = screen.getByRole('table', { name: /Places in/ })
    expect(within(places).getByRole('cell', { name: /orders-queue\s*→\s*orders-queue-dlq/ })).toBeInTheDocument()
    expect(within(places).getByText('100 (limit)')).toBeInTheDocument()
  })

  it('no components: says so and offers Connect, rather than a table of zeros', async () => {
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [], generatedAtUtc })
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([]))
    open('/next/azure')
    expect(await screen.findByText(/No Azure component is connected yet/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Connect a component/ })).toHaveAttribute('href', '/next/azure?dialog=connect')
    expect(screen.getByText('Nothing here needs a person right now.')).toBeInTheDocument()
  })

  it('an unreachable component says "Can’t reach" with the reason, and shows no counts', async () => {
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({
      components: [component({ reachable: false, lastError: 'The cloud denied access', needsYou: 0, approvals: 0, deadLetters: { value: null, kind: 'notRead' }, observedAtUtc: null, entities: [] })], generatedAtUtc,
    })
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([]))
    open('/next/azure')
    const row = within(await screen.findByRole('region', { name: /Azure PaaS components/ })).getAllByRole('row')[1]
    expect(within(row).getAllByText('Can’t reach')).toHaveLength(2) // the connection and the status
    expect(within(row).getByText('The cloud denied access')).toBeInTheDocument()
    expect(within(row).getByText('Not read')).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'In dead-letter now' })).toHaveTextContent('Can’t read')
  })

  it('shows nothing rather than wrong numbers when the components cannot be read', async () => {
    vi.mocked(componentsApi.fetchComponents).mockRejectedValue(new Error('down'))
    open('/next/azure')
    expect(await screen.findByText(/components could not be read/)).toBeInTheDocument()
    expect(screen.queryByTestId('tile-first-value')).not.toBeInTheDocument()
  })

  it('the attention table splits approvals from hand-offs, shows the family only on a hand-off, and says all channels', async () => {
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([approval(), handOff()]))
    open('/next/azure')
    const table = await screen.findByRole('region', { name: /Items that need your attention/ })
    expect(await within(table).findByText('Outcome unknown')).toBeInTheDocument()
    expect(within(table).queryByText('Policy blocked')).not.toBeInTheDocument()
    expect(within(table).getAllByText('All channels')).toHaveLength(2)
    expect(within(table).getByRole('link', { name: 'Review' })).toHaveAttribute('href', '/next/azure?dialog=review&entry=e1')
    expect(within(table).getByRole('link', { name: 'View' })).toHaveAttribute('href', '/next/azure?panel=hand-off&entry=e2')

    await userEvent.click(within(table).getByRole('tab', { name: 'Hand-offs (1)' }))
    expect(within(table).queryByRole('link', { name: 'Review' })).not.toBeInTheDocument()
    await userEvent.click(within(table).getByRole('tab', { name: 'Pending approvals (1)' }))
    expect(within(table).queryByRole('link', { name: 'View' })).not.toBeInTheDocument()
  })

  it('one cloud’s Home has no Cloud column: the page already says which cloud it is', async () => {
    open('/next/azure')
    const table = await screen.findByRole('region', { name: /Items that need your attention/ })
    await within(table).findByRole('columnheader', { name: 'Message' })
    expect(within(table).queryByRole('columnheader', { name: 'Cloud' })).not.toBeInTheDocument()
  })

  it('pages the attention table 25 at a time, and a tab starts again at page 1', async () => {
    const many = Array.from({ length: 30 }, (_, i) => approval({ id: `a${i}`, entryId: `a${i}`, dlqMessageId: 100 + i }))
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([...many, handOff()]))
    open('/next/azure')
    const table = await screen.findByRole('region', { name: /Items that need your attention/ })
    expect(await within(table).findByText('1–25 of 31')).toBeInTheDocument()
    expect(within(table).getAllByRole('row')).toHaveLength(26) // the header and 25 items

    await userEvent.click(within(table).getByRole('button', { name: 'Next page' }))
    expect(within(table).getByText('26–31 of 31')).toBeInTheDocument()
    expect(within(table).getAllByRole('row')).toHaveLength(7)

    await userEvent.click(within(table).getByRole('tab', { name: 'Hand-offs (1)' }))
    expect(within(table).getByText('1–1 of 1')).toBeInTheDocument()
  })

  it('pages the component table, and each component’s places on their own', async () => {
    const manyComponents = Array.from({ length: 30 }, (_, i) => component({ namespaceId: `ns-${i}`, name: `conn-${i}`, entities: [] }))
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: manyComponents, generatedAtUtc })
    const { unmount } = open('/next/azure')
    const table = await screen.findByRole('region', { name: /Azure PaaS components/ })
    expect(await within(table).findByText('1–25 of 30')).toBeInTheDocument()
    expect(within(table).getByRole('navigation', { name: 'Pages of components' })).toBeInTheDocument()
    unmount()

    const places = Array.from({ length: 40 }, (_, i) => entity({ name: `queue-${i}` }))
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [component({ entities: places })], generatedAtUtc })
    open('/next/azure')
    await userEvent.click(await screen.findByRole('button', { name: /Show the places/ }))
    const inner = screen.getByRole('table', { name: /Places in/ })
    expect(within(inner).getAllByRole('row')).toHaveLength(26)
    expect(screen.getByRole('navigation', { name: /Pages of places in orders-sb Service Bus/ })).toHaveTextContent('1–25 of 40')
  })

  it('an Operator sees "Needs an Approver" instead of Review, and a hand-off can still be viewed; an Approver sees Review', async () => {
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me({ governanceActive: true, effectiveRole: 'Operator', recoverRole: 'Operator', grantors: ['maya'] }))
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([approval(), handOff()]))
    const { unmount } = open('/next/azure')
    const table = await screen.findByRole('region', { name: /Items that need your attention/ })
    await waitFor(() => expect(within(table).getByText('Needs an Approver')).toBeInTheDocument())
    expect(within(table).queryByRole('link', { name: 'Review' })).not.toBeInTheDocument()
    expect(within(table).getByRole('link', { name: 'View' })).toBeInTheDocument()
    unmount()

    vi.mocked(identityApi.fetchMe).mockResolvedValue(me({ governanceActive: true, effectiveRole: 'Approver', recoverRole: 'Approver' }))
    open('/next/azure')
    expect(await screen.findByRole('link', { name: 'Review' })).toBeInTheDocument()
  })

  it('Look now is disabled for a Viewer, with the reason, and records every unwatched namespace once for an Operator', async () => {
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [awsRow(), { ...awsRow(), componentKey: 'sns', componentName: 'SNS' }], generatedAtUtc })
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me({ governanceActive: true, effectiveRole: 'Viewer', recoverRole: 'Viewer', grantors: ['maya'] }))
    const { unmount } = open('/next/aws')
    const viewerButton = await screen.findByRole('button', { name: 'Look now' })
    await waitFor(() => expect(viewerButton).toBeDisabled())
    expect(viewerButton).toHaveAttribute('title', expect.stringContaining('Operator role'))
    unmount()

    vi.mocked(identityApi.fetchMe).mockResolvedValue(me({ governanceActive: true, effectiveRole: 'Operator', recoverRole: 'Operator' }))
    vi.mocked(namespacesApi.lookAtDeadLetters).mockResolvedValue({ outcome: 'looked', queuesExamined: 2, newMessages: 5, resolved: 0, unconfirmed: 0, countsAsDeliveryAttempt: true, reason: null, lookedAtUtc: now })
    open('/next/aws')
    const button = await screen.findByRole('button', { name: 'Look now' })
    await waitFor(() => expect(button).toBeEnabled())
    await userEvent.click(button)
    await waitFor(() => expect(namespacesApi.lookAtDeadLetters).toHaveBeenCalledTimes(1)) // two components, one namespace: one look
    expect(namespacesApi.lookAtDeadLetters).toHaveBeenCalledWith('ns-aws')
  })

  it('the signature panel reads "Agent may act" from each trust level and says what the counts cover when the cloud is not watched', async () => {
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [awsRow()], generatedAtUtc })
    vi.mocked(signaturesApi.fetchSignatures).mockResolvedValue(signaturePage([signature({ provider: 'aws', reason: 'ReceiveLimitReached', messages: 110 })]))
    vi.mocked(signaturesApi.fetchSignatureTrust).mockResolvedValue(trust({ cloudCanConfirm: false }))
    open('/next/aws')
    const panel = await screen.findByRole('region', { name: 'Failure signatures' })
    expect(await within(panel).findByText('No')).toBeInTheDocument()
    expect(within(panel).getByText('ReceiveLimitReached')).toBeInTheDocument()
    expect(panel).toHaveTextContent('Covers the 200 messages recorded at the last look, not all 7,840.')
    expect(panel).toHaveTextContent('The Agent asks every time on AWS, because this cloud cannot prove a fix held.')
  })

  it('the evidence panel has one sentence per covered component, chosen by class', async () => {
    open('/next/azure')
    const panel = await screen.findByRole('region', { name: 'Evidence and replay outcome (Azure)' })
    expect(panel).toHaveTextContent('Can reach Verified when a replayed message does not return')
    expect(panel).toHaveTextContent('Not yet validated')
    expect(within(panel).getByText('Service Bus')).toBeInTheDocument()
    expect(within(panel).getByText('Storage Queues')).toBeInTheDocument()
  })

  it('an unknown cloud segment is not found, not Home', async () => {
    open('/next/oracle')
    expect(await screen.findByRole('heading', { name: 'That page doesn’t exist' })).toBeInTheDocument()
    // Only the shell's own menu-badge read (all clouds) ran; Home asked for nothing on its own.
    expect(vi.mocked(componentsApi.fetchComponents).mock.calls.every(([cloud]) => cloud === undefined)).toBe(true)
    expect(pendingApi.fetchPendingWork).not.toHaveBeenCalled()
  })

  it('has no accessibility violations on a not-watched cloud with a row expanded', async () => {
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [awsRow()], generatedAtUtc })
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([approval({ provider: 'aws' }), handOff({ provider: 'aws' })]))
    const { container } = open('/next/aws')
    await userEvent.click(await screen.findByRole('button', { name: /Show the places/ }))
    await screen.findByRole('table', { name: /Places in/ })
    await expectNoAxeViolations(container)
  })
})
