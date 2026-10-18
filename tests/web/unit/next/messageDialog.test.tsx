import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
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
import * as replayApi from '@/lib/api/replay'
import * as settingsApi from '@/lib/api/settings'
import { messageIdOf } from '@/next/lib/messageId'
import { nextRoute } from '@/next/router'
import { expectNoAxeViolations } from '@tests/support/axe'
import { agent, approval, coverageRows, handOff, me, page } from './homeData'

vi.mock('@/lib/api/coverage', async (o) => ({ ...(await o<typeof coverageApi>()), fetchCoverage: vi.fn() }))
vi.mock('@/lib/api/components', async (o) => ({ ...(await o<typeof componentsApi>()), fetchComponents: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn() }))
vi.mock('@/lib/api/pendingWork', async (o) => ({ ...(await o<typeof pendingApi>()), fetchPendingWork: vi.fn() }))
vi.mock('@/lib/api/deadLetters', async (o) => ({ ...(await o<typeof deadLettersApi>()), fetchDeadLetter: vi.fn() }))
vi.mock('@/lib/api/namespaces', async (o) => ({ ...(await o<typeof namespacesApi>()), fetchNamespace: vi.fn() }))
vi.mock('@/lib/api/replay', async (o) => ({ ...(await o<typeof replayApi>()), fetchReplays: vi.fn() }))

const at = '2026-10-18T09:00:00Z'
const detail = (over: Partial<deadLettersApi.DeadLetterDetail> = {}, item: Partial<deadLettersApi.DeadLetter> = {}): deadLettersApi.DeadLetterDetail => ({
  item: {
    id: 7, namespaceId: 'ns-azure', messageId: 'b7d1c0e5-4f9e-4a2b-8c11-aaaaaaaae8a2', sequenceNumber: 48213, entityName: 'payments-topic/subscriptions/sub-1', entityType: 'subscription',
    topicName: 'payments-topic', detectedAtUtc: at, enqueuedTimeUtc: '2026-10-18T08:00:00Z', deliveryCount: 5, sizeInBytes: 412, deadLetterReason: 'ProcessingError',
    deadLetterErrorDescription: 'Payment gateway timeout after 30s', status: 'active', ...item,
  },
  bodyPreview: '{"orderId":"A-20871","amount":129}', bodyIsPreview: false, contentType: 'application/json', correlationId: 'ord-A-20871', sessionId: null,
  applicationPropertiesJson: '{"source":"checkout-api"}', resolvedAt: null, othersLikeIt: 2, ...over,
})

function open(url: string, d = detail()) {
  vi.mocked(deadLettersApi.fetchDeadLetter).mockResolvedValue(d)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter([nextRoute], { initialEntries: [url] })
  render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>)
  return router
}
const url = '/next/azure?dialog=message&id=7'
const dialog = async () => within(await screen.findByRole('dialog'))

describe('messageIdOf', () => {
  it('accepts whole digits only, so nothing else is ever asked of the API', () => {
    expect(messageIdOf('7')).toBe(7)
    expect(messageIdOf('0042')).toBe(42)
    for (const bad of ['', 'x', '7x', '-1', '1.5', '7 or 1=1', null]) expect(messageIdOf(bad), String(bad)).toBeNull()
  })
})

describe('the Message dialog (unit D3)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [], generatedAtUtc: at })
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([]))
    vi.mocked(namespacesApi.fetchNamespace).mockResolvedValue({ id: 'ns-azure', name: 'orders-sb', displayName: null } as namespacesApi.Namespace)
    vi.mocked(replayApi.fetchReplays).mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 25 })
  })

  it('opens over the page from the address, titled with a short form of the message id, and asks only for that message', async () => {
    open(url)
    const d = await dialog()
    expect(await d.findByRole('heading', { name: /Message b7d1c0…aaae8a2|Message b7d1c0…/ })).toBeInTheDocument()
    expect(deadLettersApi.fetchDeadLetter).toHaveBeenCalledWith(7)
    expect(await d.findByText(/orders-sb · payments-topic \/ sub-1 · In dead-letter/)).toBeInTheDocument()
  })

  it('Summary says why it failed, how many are like it, and the properties; nothing shows a 0 it did not measure', async () => {
    open(url)
    const d = await dialog()
    const why = await d.findByRole('region', { name: 'Why it failed' })
    expect(why).toHaveTextContent('ReasonProcessingError')
    expect(why).toHaveTextContent('DescriptionPayment gateway timeout after 30s')
    expect(why).toHaveTextContent('Others like it2 more in this queue with the same reason')
    expect(why).toHaveTextContent('Deliveries5')
    const props = d.getByRole('region', { name: 'Message properties' })
    expect(props).toHaveTextContent('Sequence number48,213')
    expect(props).toHaveTextContent('SessionNone')
    expect(props).toHaveTextContent('Content typeapplication/json')
  })

  it('has Body, Properties and History tabs, and the body is the stored text', async () => {
    open(url)
    const d = await dialog()
    await d.findByRole('region', { name: 'Why it failed' })
    await userEvent.click(d.getByRole('button', { name: 'Body' }))
    expect(await d.findByLabelText('Message body text')).toHaveTextContent('A-20871')
    await userEvent.click(d.getByRole('button', { name: 'Properties' }))
    expect(d.getByText('checkout-api')).toBeInTheDocument()
    await userEvent.click(d.getByRole('button', { name: 'History' }))
    expect(d.getByText('ServiceHub recorded it in dead-letter')).toBeInTheDocument()
  })

  it('History lists what happened, newest first: a replay, the Agent asking, and the first record', async () => {
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([approval({ dlqMessageId: 7, entryId: 'e1', since: '2026-10-18T10:00:00Z' })]))
    vi.mocked(replayApi.fetchReplays).mockResolvedValue({
      items: [{ id: 1, dlqMessageId: 7, namespaceId: 'ns-azure', provider: 'azure', messageId: 'm', sourceEntity: 's', targetEntity: 'payments-topic', replayedAt: '2026-10-18T11:00:00Z', replayedBy: 'maya', outcomeStatus: 'accepted' } as replayApi.ReplayListItem],
      total: 1, page: 1, pageSize: 25,
    })
    open(url)
    const d = await dialog()
    await d.findByRole('region', { name: 'Why it failed' })
    await userEvent.click(d.getByRole('button', { name: 'History' }))
    const rows = (await d.findAllByRole('row')).slice(1).map((r) => r.textContent)
    expect(rows[0]).toContain('Replayed by maya to payments-topic — accepted by the cloud')
    expect(rows[1]).toContain('The Agent asked for approval to replay it')
    expect(rows[2]).toContain('ServiceHub recorded it in dead-letter')
  })

  it('says where it stands from the pending-work read, and offers Review only to someone who may approve', async () => {
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([approval({ dlqMessageId: 7, entryId: 'e1' })]))
    open(url)
    const d = await dialog()
    expect(await d.findByText(/Waiting for approval/)).toBeInTheDocument()
    expect(await d.findByRole('link', { name: 'Review…' })).toHaveAttribute('href', '/next/azure?dialog=review&entry=e1')
  })

  it('a Viewer is told it needs an Approver, with no Review link', async () => {
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me({ governanceActive: true, effectiveRole: 'Viewer', recoverRole: 'Viewer', grantors: ['maya'] }))
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([approval({ dlqMessageId: 7 })]))
    open(url)
    const d = await dialog()
    expect(await d.findByText('Needs an Approver to review')).toBeInTheDocument()
    expect(d.queryByRole('link', { name: 'Review…' })).not.toBeInTheDocument()
  })

  it('a hand-off is "Handed to a person" and offers no Review, since there is nothing to approve', async () => {
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([handOff({ dlqMessageId: 7 })]))
    open(url)
    const d = await dialog()
    expect(await d.findByText(/Handed to a person/)).toBeInTheDocument()
    expect(d.queryByRole('link', { name: 'Review…' })).not.toBeInTheDocument()
  })

  it('is read-only and says so, and offers Copy body but never a way to edit', async () => {
    open(url)
    const d = await dialog()
    expect(await d.findByRole('note')).toHaveTextContent('Read-only. ServiceHub never changes a message’s body.')
    expect(d.getAllByRole('button', { name: 'Copy body' })).toHaveLength(1) // the body block's own control, not a second one
    expect(d.queryByRole('textbox')).not.toBeInTheDocument()
  })

  it('a message with no kept body says so rather than drawing an empty block', async () => {
    open(url, detail({ bodyPreview: null }))
    const d = await dialog()
    await d.findByRole('region', { name: 'Why it failed' })
    expect(d.getByText('ServiceHub did not keep a body for this message.')).toBeInTheDocument()
    expect(d.queryByRole('button', { name: 'Copy body' })).not.toBeInTheDocument()
  })

  it('an id that is not whole digits asks the API for nothing', async () => {
    open('/next/azure?dialog=message&id=7%20or%201%3D1')
    const d = await dialog()
    expect(await d.findByText('This address names no message.')).toBeInTheDocument()
    expect(deadLettersApi.fetchDeadLetter).not.toHaveBeenCalled()
  })

  it('a message that cannot be read says so plainly', async () => {
    const router = open(url)
    vi.mocked(deadLettersApi.fetchDeadLetter).mockRejectedValue(new Error('gone'))
    router.dispose()
    document.body.innerHTML = ''
    vi.mocked(deadLettersApi.fetchDeadLetter).mockRejectedValue(new Error('gone'))
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(<QueryClientProvider client={client}><RouterProvider router={createMemoryRouter([nextRoute], { initialEntries: [url] })} /></QueryClientProvider>)
    const d = await dialog()
    expect(await d.findByText(/That message could not be read/)).toBeInTheDocument()
  })

  it('Close closes it and keeps the page', async () => {
    const router = open(url)
    const d = await dialog()
    await d.findByRole('region', { name: 'Why it failed' })
    await userEvent.click(d.getAllByRole('button', { name: 'Close' })[0])
    await vi.waitFor(() => expect(router.state.location.search).toBe(''))
  })

  it('has no accessibility violations', async () => {
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([approval({ dlqMessageId: 7 })]))
    open(url)
    const d = await dialog()
    await d.findByRole('link', { name: 'Review…' })
    await expectNoAxeViolations(document.body)
  })
})
