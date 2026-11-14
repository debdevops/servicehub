import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as agentsApi from '@/lib/api/agents'
import * as componentsApi from '@/lib/api/components'
import * as coverageApi from '@/lib/api/coverage'
import * as deadLettersApi from '@/lib/api/deadLetters'
import * as identityApi from '@/lib/api/identity'
import * as messagesApi from '@/lib/api/messages'
import * as namespacesApi from '@/lib/api/namespaces'
import * as pendingApi from '@/lib/api/pendingWork'
import * as settingsApi from '@/lib/api/settings'
import * as signaturesApi from '@/lib/api/signatures'
import { nextRoute } from '@/next/router'
import { agent, component, coverageRows, entity, me, page } from './homeData'

vi.mock('@/lib/api/coverage', async (o) => ({ ...(await o<typeof coverageApi>()), fetchCoverage: vi.fn() }))
vi.mock('@/lib/api/components', async (o) => ({ ...(await o<typeof componentsApi>()), fetchComponent: vi.fn(), fetchComponents: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn() }))
vi.mock('@/lib/api/pendingWork', async (o) => ({ ...(await o<typeof pendingApi>()), fetchPendingWork: vi.fn() }))
vi.mock('@/lib/api/signatures', async (o) => ({ ...(await o<typeof signaturesApi>()), fetchSignatures: vi.fn(), fetchSignatureTrust: vi.fn() }))
vi.mock('@/lib/api/namespaces', async (o) => ({ ...(await o<typeof namespacesApi>()), fetchNamespace: vi.fn(), fetchEntities: vi.fn(), fetchNamespaces: vi.fn(), lookAtDeadLetters: vi.fn() }))
vi.mock('@/lib/api/deadLetters', async (o) => ({ ...(await o<typeof deadLettersApi>()), fetchDeadLetters: vi.fn() }))
vi.mock('@/lib/api/messages', async (o) => ({ ...(await o<typeof messagesApi>()), peekMessages: vi.fn(), peekTransferDeadLetters: vi.fn() }))

const at = '2026-10-18T10:00:00Z'
const ns = () => ({
  id: 'ns-azure', name: 'orders-sb', displayName: null, description: null, provider: 'azure', environment: 'dev', authType: 'connectionString', awsRegion: null, gcpProjectId: null,
  isActive: true, createdAt: at, lastConnectionTestAt: null, lastConnectionTestSucceeded: true,
  capabilities: { supportsMessageCounts: true, supportsRepeatablePeek: true, countsAreApproximate: false, canProveDlqAbsence: true },
}) as unknown as namespacesApi.Namespace
const msg = (n: number, over: Partial<messagesApi.Message> = {}): messagesApi.Message => ({
  messageId: `m-${n}`, sequenceNumber: n, body: null, contentType: null, correlationId: null, sessionId: null, subject: null, enqueuedTime: at, deliveryCount: 1,
  deadLetterReason: null, deadLetterErrorDescription: null, applicationProperties: null, sizeInBytes: 1, isFromDeadLetter: false, ...over,
})
const peeked = (messages: messagesApi.Message[]): messagesApi.PeekPage => ({ namespaceId: 'ns-azure', entity: 'orders-queue', subscription: null, deadLetter: false, messages, paging: { requested: 100, returned: messages.length, nextFromSequenceNumber: null }, peek: { repeatable: true, warning: null } })

function open(url: string, transfer: number | null = null) {
  const c = component({ entities: [entity({ name: 'orders-queue' })] })
  vi.mocked(componentsApi.fetchComponent).mockResolvedValue(c)
  vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [c], generatedAtUtc: at })
  vi.mocked(namespacesApi.fetchNamespace).mockResolvedValue(ns())
  vi.mocked(namespacesApi.fetchEntities).mockResolvedValue({ namespaceId: 'ns-azure', entities: [{ name: 'orders-queue', kind: 'queue', activeMessages: 3, deadLetterMessages: 2, deadLetterTargetName: null, transferDeadLetterMessages: transfer }] } as namespacesApi.EntityList)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}><RouterProvider router={createMemoryRouter([nextRoute], { initialEntries: [url] })} /></QueryClientProvider>)
}
const ok = '/azure/c/service-bus/ns-azure'

describe('G4 transfer dead-letter queue (read only)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([]))
    vi.mocked(deadLettersApi.fetchDeadLetters).mockResolvedValue({ items: [], paging: { total: 0, page: 1, pageSize: 5 }, groups: [], otherReasons: null, entities: [] })
  })

  it('shows no column and no section where the cloud has no transfer dead-letter queue', async () => {
    open(ok, null)
    await screen.findByTestId('tile-first')
    expect(screen.queryByRole('columnheader', { name: /Transfer dead-letter/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Transfer dead-letter queues' })).not.toBeInTheDocument()
  })

  it('shows the count as a column, and reads the queue only when asked, with no replay offered', async () => {
    vi.mocked(messagesApi.peekTransferDeadLetters).mockResolvedValue(peeked([msg(7, { deadLetterReason: 'TransactionFailed', deadLetterErrorDescription: 'could not reach next-hop' })]))
    open(ok, 4)
    const table = await screen.findByRole('table', { name: /Queues, topics and subscriptions/ })
    expect(within(table).getByRole('columnheader', { name: /Transfer dead-letter/ })).toBeInTheDocument()
    const section = await screen.findByRole('region', { name: 'Transfer dead-letter queues' })
    expect(messagesApi.peekTransferDeadLetters).not.toHaveBeenCalled()
    await userEvent.click(within(section).getByRole('button', { name: 'Read them' }))
    expect(await within(section).findByText('TransactionFailed')).toBeInTheDocument()
    expect(messagesApi.peekTransferDeadLetters).toHaveBeenCalledWith('ns-azure', { entity: 'orders-queue', subscription: undefined, max: 50 })
    expect(within(section).queryByRole('button', { name: /replay/i })).not.toBeInTheDocument()
  })
})

describe('G5 deferred messages tab (read only)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([]))
  })

  it('looks only when asked, and lists only the deferred ones among what was peeked', async () => {
    vi.mocked(messagesApi.peekMessages).mockResolvedValue(peeked([msg(1), msg(2, { isDeferred: true }), msg(3, { isDeferred: true })]))
    open(`${ok}?tab=stuck`)
    expect(await screen.findByText(/Service Bus keeps no count of deferred messages/)).toBeInTheDocument()
    expect(messagesApi.peekMessages).not.toHaveBeenCalled()
    await userEvent.click(screen.getByRole('button', { name: 'Look now' }))
    const table = await screen.findByRole('table', { name: /Deferred messages in orders-queue/ })
    await waitFor(() => expect(within(table).getAllByRole('row')).toHaveLength(3)) // header + the two deferred, not the ordinary one
    expect(within(table).queryByText('m-1')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /review|move|resend/i })).not.toBeInTheDocument()
  })

  it('says plainly when none is among what was looked at', async () => {
    vi.mocked(messagesApi.peekMessages).mockResolvedValue(peeked([msg(1)]))
    open(`${ok}?tab=stuck`)
    await userEvent.click(await screen.findByRole('button', { name: 'Look now' }))
    expect(await screen.findByText(/No deferred message among the first 1 looked at/)).toBeInTheDocument()
  })
})
