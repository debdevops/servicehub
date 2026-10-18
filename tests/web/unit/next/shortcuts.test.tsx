import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
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
import { nextRoute } from '@/next/router'
import { nextShortcuts } from '@/next/content/shortcuts'
import { agent, component, coverageRows, me, page } from './homeData'

vi.mock('@/lib/api/coverage', async (o) => ({ ...(await o<typeof coverageApi>()), fetchCoverage: vi.fn() }))
vi.mock('@/lib/api/components', async (o) => ({ ...(await o<typeof componentsApi>()), fetchComponents: vi.fn(), fetchComponent: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn() }))
vi.mock('@/lib/api/pendingWork', async (o) => ({ ...(await o<typeof pendingApi>()), fetchPendingWork: vi.fn() }))
vi.mock('@/lib/api/deadLetters', async (o) => ({ ...(await o<typeof deadLettersApi>()), fetchDeadLetters: vi.fn(), fetchDeadLetter: vi.fn() }))
vi.mock('@/lib/api/replay', async (o) => ({ ...(await o<typeof replayApi>()), fetchReplays: vi.fn(), fetchReplayProposal: vi.fn(), replayMessage: vi.fn() }))
vi.mock('@/lib/api/namespaces', async (o) => ({ ...(await o<typeof namespacesApi>()), fetchNamespaces: vi.fn(), fetchNamespace: vi.fn(), fetchEntities: vi.fn() }))

const at = '2026-10-18T10:00:00Z'
const detail = (status = 'active') => ({
  item: { id: 7, messageId: 'm-7', namespaceId: 'ns-azure', entityName: 'orders-queue', entityType: 'queue', topicName: null, detectedAtUtc: at, enqueuedTimeUtc: at, sequenceNumber: 7, sizeInBytes: 10, deadLetterReason: 'Timeout', deadLetterErrorDescription: 'x', deliveryCount: 3, status },
  bodyPreview: '{}', bodyIsPreview: false, othersLikeIt: 0, resolvedAt: null, correlationId: null, sessionId: null, contentType: null, applicationPropertiesJson: null,
}) as never

function open(u: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter([nextRoute], { initialEntries: [u] })
  return { router, ...render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>) }
}

beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
  vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [component()], generatedAtUtc: at })
  vi.mocked(componentsApi.fetchComponent).mockResolvedValue(component())
  vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
  vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
  vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
  vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([]))
  vi.mocked(namespacesApi.fetchNamespaces).mockResolvedValue([])
  vi.mocked(namespacesApi.fetchNamespace).mockResolvedValue({ id: 'ns-azure', name: 'orders-sb', provider: 'azure', environment: 'dev', capabilities: { supportsMessageCounts: true, supportsRepeatablePeek: true, countsAreApproximate: false } } as never)
  vi.mocked(namespacesApi.fetchEntities).mockResolvedValue({ namespaceId: 'ns-azure', entities: [] })
  vi.mocked(deadLettersApi.fetchDeadLetter).mockResolvedValue(detail())
  vi.mocked(deadLettersApi.fetchDeadLetters).mockResolvedValue({ items: [], paging: { total: 0, page: 1, pageSize: 25 }, groups: [], otherReasons: null, entities: [] })
  vi.mocked(replayApi.fetchReplays).mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 25 })
  vi.mocked(replayApi.fetchReplayProposal).mockRejectedValue(new Error('not needed'))
})

describe('Keyboard shortcuts', () => {
  it('lists exactly the keys that have a test below, so a line cannot outlive its shortcut', () => {
    expect(nextShortcuts.map((s) => s.keys)).toEqual(['⌘K or Ctrl-K', '?', 'R', '/', 'Esc'])
  })

  it('Ctrl-K and ⌘K open Search; Esc closes it', async () => {
    const user = userEvent.setup()
    const { router } = open('/next/azure')
    await screen.findByRole('heading', { level: 1, name: /Dashboard/ })
    await user.keyboard('{Control>}k{/Control}')
    await waitFor(() => expect(router.state.location.search).toBe('?dialog=search'))
    await user.keyboard('{Escape}')
    await waitFor(() => expect(router.state.location.search).toBe(''))
    await user.keyboard('{Meta>}k{/Meta}')
    await waitFor(() => expect(router.state.location.search).toBe('?dialog=search'))
  })

  it('? opens Help, from any page, and does not stack a second one', async () => {
    const user = userEvent.setup()
    const { router } = open('/next/azure/replays?window=30d')
    await screen.findByRole('heading', { level: 1, name: 'Replay history' })
    await user.keyboard('?')
    await waitFor(() => expect(router.state.location.search).toContain('panel=help'))
    expect(router.state.location.search).toContain('window=30d') // the page's own parameters stay
    const before = router.state.location.search
    await user.keyboard('?')
    expect(router.state.location.search).toBe(before)
  })

  it('/ moves focus to the list’s search box', async () => {
    const user = userEvent.setup()
    open('/next/azure/c/service-bus/ns-azure?tab=dead-letters')
    const box = await screen.findByLabelText('Search messages')
    expect(box).not.toHaveFocus()
    await user.keyboard('/')
    expect(box).toHaveFocus()
  })

  it('R presses the open message’s Replay link — the preview, never a replay', async () => {
    const user = userEvent.setup()
    const { router } = open('/next/azure?dialog=message&id=7')
    await screen.findByRole('link', { name: 'Replay…' })
    await user.keyboard('r')
    await waitFor(() => expect(router.state.location.search).toBe('?dialog=replay&id=7'))
    expect(replayApi.replayMessage).not.toHaveBeenCalled()
  })

  it('R does nothing where the Replay link is not offered (a message no longer in dead-letter)', async () => {
    const user = userEvent.setup()
    vi.mocked(deadLettersApi.fetchDeadLetter).mockResolvedValue(detail('resolved'))
    const { router } = open('/next/azure?dialog=message&id=7')
    await screen.findByText('Why it failed') // the message has loaded, and offers no Replay link
    expect(screen.queryByRole('link', { name: 'Replay…' })).not.toBeInTheDocument()
    await user.keyboard('r')
    expect(router.state.location.search).toBe('?dialog=message&id=7')
  })

  it('R does nothing for someone who may not replay', async () => {
    const user = userEvent.setup()
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me({ governanceActive: true, effectiveRole: 'Viewer', recoverRole: 'Viewer' }))
    const { router } = open('/next/azure?dialog=message&id=7')
    expect(await screen.findByText(/Needs an Operator to replay/)).toBeInTheDocument()
    await user.keyboard('r')
    expect(router.state.location.search).toBe('?dialog=message&id=7')
  })

  it('a single key never fires inside a text field, or with a modifier', async () => {
    const user = userEvent.setup()
    const { router } = open('/next/azure/c/service-bus/ns-azure?tab=dead-letters')
    const box = await screen.findByLabelText('Search messages')
    await user.click(box)
    await user.keyboard('?/r')
    expect(router.state.location.search).not.toContain('panel=help')
    expect(box).toHaveValue('?/r')
    await user.tab() // out of the field
    await user.keyboard('{Alt>}?{/Alt}')
    expect(router.state.location.search).not.toContain('panel=help')
  })

  it('has no “A” switch: there is no Simple / Advanced any more', async () => {
    const user = userEvent.setup()
    const { router } = open('/next/azure')
    await screen.findByRole('heading', { level: 1, name: /Dashboard/ })
    await user.keyboard('a')
    expect(router.state.location.pathname).toBe('/next/azure')
  })

  it('Help lists the same shortcuts', async () => {
    open('/next/azure?panel=help')
    const dialog = await screen.findByRole('dialog')
    for (const k of nextShortcuts) expect(dialog.textContent).toContain(k.keys)
  })
})
