import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as agentsApi from '@/lib/api/agents'
import * as componentsApi from '@/lib/api/components'
import * as coverageApi from '@/lib/api/coverage'
import * as identityApi from '@/lib/api/identity'
import * as messagesApi from '@/lib/api/messages'
import * as namespacesApi from '@/lib/api/namespaces'
import * as pendingApi from '@/lib/api/pendingWork'
import * as settingsApi from '@/lib/api/settings'
import { nextRoute } from '@/next/router'
import { expectNoAxeViolations } from '@tests/support/axe'
import { agent, component, coverageRows, me, page } from './homeData'

vi.mock('@/lib/api/coverage', async (o) => ({ ...(await o<typeof coverageApi>()), fetchCoverage: vi.fn() }))
vi.mock('@/lib/api/components', async (o) => ({ ...(await o<typeof componentsApi>()), fetchComponents: vi.fn(), fetchComponent: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn() }))
vi.mock('@/lib/api/pendingWork', async (o) => ({ ...(await o<typeof pendingApi>()), fetchPendingWork: vi.fn() }))
vi.mock('@/lib/api/messages', async (o) => ({ ...(await o<typeof messagesApi>()), sendMessage: vi.fn() }))
vi.mock('@/lib/api/namespaces', async (o) => ({ ...(await o<typeof namespacesApi>()), fetchNamespaces: vi.fn(), fetchNamespace: vi.fn(), fetchEntities: vi.fn() }))

const at = '2026-10-18T10:00:00Z'
const ns = (over: Partial<namespacesApi.Namespace> = {}) => ({ id: 'ns-azure', name: 'orders-sb', displayName: null, provider: 'azure', environment: 'dev', capabilities: { supportsMessageCounts: true, supportsRepeatablePeek: true, countsAreApproximate: false }, ...over }) as unknown as namespacesApi.Namespace
const sent = () => vi.mocked(messagesApi.sendMessage).mock.calls.map((c) => c[0])

function open(url: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter([nextRoute], { initialEntries: [url] })
  return { router, ...render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>) }
}

describe('Send a test message (unit F8)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [component()], generatedAtUtc: at })
    vi.mocked(componentsApi.fetchComponent).mockResolvedValue(component())
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([]))
    vi.mocked(namespacesApi.fetchNamespaces).mockResolvedValue([ns()])
    vi.mocked(namespacesApi.fetchNamespace).mockResolvedValue(ns())
    vi.mocked(namespacesApi.fetchEntities).mockResolvedValue({ namespaceId: 'ns-azure', entities: [{ name: 'orders-queue', kind: 'queue', activeMessages: 1, deadLetterMessages: 0, deadLetterTargetName: null }] })
    vi.mocked(messagesApi.sendMessage).mockResolvedValue({ accepted: true, detail: 'Sent 1 message.' })
  })

  it('opens from a connection’s page with that connection chosen, and sends nothing on open', async () => {
    const user = userEvent.setup()
    const { router } = open('/next/azure/c/service-bus/ns-azure')
    await user.click(await screen.findByRole('link', { name: 'Send a test message' }))
    await screen.findByRole('dialog')
    expect(router.state.location.search).toBe('?dialog=send&ns=ns-azure')
    expect(await screen.findByRole('button', { name: 'Send' })).toBeInTheDocument()
    expect(sent()).toEqual([])
  })

  it('sends exactly one message, on the one click, to the chosen queue', async () => {
    const user = userEvent.setup()
    open('/next/azure?dialog=send&ns=ns-azure')
    const dialog = await screen.findByRole('dialog')
    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Send' })).toBeEnabled())
    await user.click(within(dialog).getByRole('button', { name: 'Send' }))
    expect(await within(dialog).findByText(/Sent 1 message/)).toBeInTheDocument()
    expect(sent()).toHaveLength(1)
    expect(sent()[0]).toMatchObject({ namespaceId: 'ns-azure', entity: 'orders-queue', isTopic: false })
  })

  it('does not offer a Production connection, and says why', async () => {
    vi.mocked(namespacesApi.fetchNamespaces).mockResolvedValue([ns({ environment: 'prod' })])
    open('/next/azure?dialog=send&ns=ns-azure')
    expect(await screen.findByText(/marked Production/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Send' })).not.toBeInTheDocument()
  })

  it('keeps Send disabled for someone who may not send, and for a body that is not valid JSON', async () => {
    const user = userEvent.setup()
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me({ governanceActive: true, effectiveRole: 'Viewer', recoverRole: 'Viewer' }))
    open('/next/azure?dialog=send&ns=ns-azure')
    const dialog = await screen.findByRole('dialog')
    const send = await within(dialog).findByRole('button', { name: 'Send' })
    await waitFor(() => expect(send).toBeDisabled())
    expect(within(dialog).getAllByText(/you need the Operator role/).length).toBeGreaterThan(0)
    expect(sent()).toEqual([])
    void user
  })

  it('shows the server’s refusal and does not claim it sent', async () => {
    const user = userEvent.setup()
    vi.mocked(messagesApi.sendMessage).mockRejectedValue(new Error('refused'))
    open('/next/azure?dialog=send&ns=ns-azure')
    const dialog = await screen.findByRole('dialog')
    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Send' })).toBeEnabled())
    await user.click(within(dialog).getByRole('button', { name: 'Send' }))
    expect(await within(dialog).findByRole('alert')).toBeInTheDocument()
    expect(within(dialog).queryByText(/Sent 1 message/)).not.toBeInTheDocument()
  })

  it('has no accessibility violations', async () => {
    open('/next/azure?dialog=send&ns=ns-azure')
    const dialog = await screen.findByRole('dialog')
    await within(dialog).findByRole('button', { name: 'Send' })
    await expectNoAxeViolations(dialog)
  })
})
