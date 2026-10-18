import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as agentsApi from '@/lib/api/agents'
import * as bulkApi from '@/lib/api/bulk'
import * as componentsApi from '@/lib/api/components'
import * as coverageApi from '@/lib/api/coverage'
import * as deadLettersApi from '@/lib/api/deadLetters'
import * as identityApi from '@/lib/api/identity'
import * as namespacesApi from '@/lib/api/namespaces'
import * as pendingApi from '@/lib/api/pendingWork'
import * as settingsApi from '@/lib/api/settings'
import { bulkSelection } from '@/lib/bulkSelection'
import { nextRoute } from '@/next/router'
import { expectNoAxeViolations } from '@tests/support/axe'
import { agent, approval, component, coverageRows, me, page } from './homeData'

vi.mock('@/lib/api/coverage', async (o) => ({ ...(await o<typeof coverageApi>()), fetchCoverage: vi.fn() }))
vi.mock('@/lib/api/components', async (o) => ({ ...(await o<typeof componentsApi>()), fetchComponent: vi.fn(), fetchComponents: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn() }))
vi.mock('@/lib/api/pendingWork', async (o) => ({ ...(await o<typeof pendingApi>()), fetchPendingWork: vi.fn() }))
vi.mock('@/lib/api/deadLetters', async (o) => ({ ...(await o<typeof deadLettersApi>()), fetchDeadLetters: vi.fn(), fetchDeadLetter: vi.fn() }))
vi.mock('@/lib/api/bulk', async (o) => ({ ...(await o<typeof bulkApi>()), previewBulk: vi.fn(), startBulk: vi.fn(), fetchBulk: vi.fn(), cancelBulk: vi.fn() }))
vi.mock('@/lib/api/namespaces', async (o) => ({ ...(await o<typeof namespacesApi>()), fetchNamespace: vi.fn(), fetchEntities: vi.fn(), fetchNamespaces: vi.fn(), lookAtDeadLetters: vi.fn() }))

const at = '2026-10-18T09:00:00Z'
const letter = (id: number, over: Partial<deadLettersApi.DeadLetter> = {}) => ({
  id, namespaceId: 'ns-azure', messageId: `m-${id}`, sequenceNumber: id, entityName: 'orders-queue', entityType: 'queue', topicName: null, detectedAtUtc: at, enqueuedTimeUtc: at,
  deliveryCount: 5, sizeInBytes: 10, deadLetterReason: 'ProcessingError', deadLetterErrorDescription: 'Timed out', status: 'active', ...over,
}) as deadLettersApi.DeadLetter
const list = (items: deadLettersApi.DeadLetter[], total = items.length): deadLettersApi.DeadLetterPage => ({
  items, paging: { total, page: 1, pageSize: 25 }, groups: [{ reason: 'ProcessingError', count: total }], otherReasons: null, entities: ['orders-queue'],
})
const preview = (over: Partial<bulkApi.BulkPreview> = {}): bulkApi.BulkPreview => ({
  previewId: 'pv1', selected: 2, willReplay: 2, heldBackCount: 0, groups: [{ reason: 'ProcessingError', selected: 2, willReplay: 2, heldBack: 0 }], heldBack: [], perSecond: 5,
  stopAfterConsecutiveFailures: 3, expiresInMinutes: 10, canProveDlqAbsence: true, ...over,
})
const ok = '/next/azure/c/service-bus/ns-azure?tab=dead-letters'

function open(url: string) {
  const c = component()
  vi.mocked(componentsApi.fetchComponent).mockResolvedValue(c)
  vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [c], generatedAtUtc: at })
  vi.mocked(namespacesApi.fetchNamespace).mockResolvedValue({ id: 'ns-azure', name: 'orders-sb', displayName: null, provider: 'azure', environment: 'dev', capabilities: { supportsMessageCounts: true, supportsRepeatablePeek: true, supportsPurge: false, countsAreApproximate: false } } as unknown as namespacesApi.Namespace)
  vi.mocked(namespacesApi.fetchNamespaces).mockResolvedValue([])
  vi.mocked(namespacesApi.fetchEntities).mockResolvedValue({ namespaceId: 'ns-azure', entities: [] })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter([nextRoute], { initialEntries: [url] })
  render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>)
  return router
}

describe('Bulk replay (unit F3)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    bulkSelection.set(null)
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([]))
    vi.mocked(deadLettersApi.fetchDeadLetters).mockResolvedValue(list([letter(1), letter(2), letter(3, { status: 'resolved' })]))
    vi.mocked(deadLettersApi.fetchDeadLetter).mockRejectedValue(new Error('not needed'))
    vi.mocked(bulkApi.previewBulk).mockResolvedValue(preview())
  })

  it('lets only a message that is still stuck and not held for a person be ticked', async () => {
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([approval({ id: 'a1', entryId: 'a1', dlqMessageId: 2 })]))
    open(ok)
    await screen.findByRole('table', { name: /Dead-letter messages/ })
    expect(screen.getByLabelText('Select message m-1')).toBeInTheDocument()
    expect(screen.queryByLabelText('Select message m-2')).not.toBeInTheDocument() // Review answers a held replay
    expect(screen.queryByLabelText('Select message m-3')).not.toBeInTheDocument() // no longer stuck
  })

  it('hands exactly the ticked ids to the dialog, which shows a PREVIEW and sends nothing until the one click', async () => {
    const user = userEvent.setup()
    const router = open(ok)
    await user.click(await screen.findByLabelText('Select message m-1'))
    await user.click(screen.getByLabelText('Select message m-2'))
    const link = (await screen.findAllByRole('link', { name: 'Replay selected…' }))[0]
    expect(link).toHaveAttribute('href', expect.stringContaining('dialog=bulk-replay'))
    await user.click(link)
    expect(bulkSelection.get()).toEqual({ ids: [1, 2] })
    const dialog = await screen.findByRole('dialog')
    await waitFor(() => expect(bulkApi.previewBulk).toHaveBeenCalledWith([1, 2]))
    expect(await within(dialog).findByText(/Preview — nothing has run yet/)).toBeInTheDocument()
    expect(bulkApi.startBulk).not.toHaveBeenCalled()
    expect(router.state.location.search).toContain('dialog=bulk-replay')
  })

  it('starts a run only from the stored preview, on the one click, and carries its id in the address', async () => {
    const user = userEvent.setup()
    vi.mocked(bulkApi.startBulk).mockResolvedValue({ id: 'job-1' } as never)
    vi.mocked(bulkApi.fetchBulk).mockResolvedValue({ id: 'job-1', status: 'running', selected: 2, willReplay: 2, sent: 0, failed: 0, unknown: 0, remaining: 2, heldBack: 0, sampleOnly: false, endedReason: null, previewedAt: at, startedAt: at, endedAt: null } as never)
    const router = open(ok)
    await user.click(await screen.findByLabelText('Select message m-1'))
    await user.click((await screen.findAllByRole('link', { name: 'Replay selected…' }))[0])
    const dialog = await screen.findByRole('dialog')
    // The dialog repeats the one button at the top and the foot.
    await user.click((await within(dialog).findAllByRole('button', { name: /^Replay 2 messages/ }))[0])
    await waitFor(() => expect(bulkApi.startBulk).toHaveBeenCalledWith('pv1', false))
    expect(bulkApi.startBulk).toHaveBeenCalledTimes(1)
    await waitFor(() => expect(router.state.location.search).toContain('job=job-1'))
  })

  it('says to choose messages first when it is opened with nothing chosen, and previews nothing', async () => {
    open(`${ok}&dialog=bulk-replay`)
    expect(await screen.findByText(/Choose some dead letters in the table first/)).toBeInTheDocument()
    expect(bulkApi.previewBulk).not.toHaveBeenCalled()
  })

  it('does not preview, and says nothing was sent, when the preview cannot be worked out', async () => {
    const user = userEvent.setup()
    vi.mocked(bulkApi.previewBulk).mockRejectedValue(new Error('boom'))
    open(ok)
    await user.click(await screen.findByLabelText('Select message m-1'))
    await user.click((await screen.findAllByRole('link', { name: 'Replay selected…' }))[0])
    expect(await screen.findByText(/so nothing was sent/)).toBeInTheDocument()
    expect(bulkApi.startBulk).not.toHaveBeenCalled()
  })

  it('has no accessibility violations on the preview', async () => {
    const user = userEvent.setup()
    open(ok)
    await user.click(await screen.findByLabelText('Select message m-1'))
    await user.click((await screen.findAllByRole('link', { name: 'Replay selected…' }))[0])
    const dialog = await screen.findByRole('dialog')
    await within(dialog).findByText(/Preview — nothing has run yet/)
    await expectNoAxeViolations(dialog)
  })
})
