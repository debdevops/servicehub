import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as agentsApi from '@/lib/api/agents'
import * as componentsApi from '@/lib/api/components'
import * as coverageApi from '@/lib/api/coverage'
import * as identityApi from '@/lib/api/identity'
import * as pendingApi from '@/lib/api/pendingWork'
import * as settingsApi from '@/lib/api/settings'
import { nextRoute } from '@/next/router'
import { nextHelpAnswers } from '@/next/content/help'
import { searchNext } from '@/next/lib/searchIndex'
import { expectNoAxeViolations } from '@tests/support/axe'
import { agent, component, coverageRows, entity, me, page } from './homeData'

vi.mock('@/lib/api/coverage', async (o) => ({ ...(await o<typeof coverageApi>()), fetchCoverage: vi.fn() }))
vi.mock('@/lib/api/components', async (o) => ({ ...(await o<typeof componentsApi>()), fetchComponents: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn() }))
vi.mock('@/lib/api/pendingWork', async (o) => ({ ...(await o<typeof pendingApi>()), fetchPendingWork: vi.fn() }))

const at = '2026-10-18T10:00:00Z'
const withQueue = component({ entities: [entity({ name: 'orders-queue' }), entity({ name: 'sub-1', parent: 'payments-topic', kind: 'subscription' }), entity({ name: 'payments-topic', kind: 'topic' })] })

function open(url: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter([nextRoute], { initialEntries: [url] })
  return { router, ...render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>) }
}

beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
  vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [withQueue], generatedAtUtc: at })
  vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
  vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
  vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
  vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([]))
})

describe('Help (unit F6)', () => {
  it('opens from the header, filters its answers as you type, and links to pages in the cloud you are on', async () => {
    const user = userEvent.setup()
    open('/next/azure')
    await user.click(await screen.findByRole('link', { name: 'Help' }))
    const panel = await screen.findByRole('dialog')
    const box = await within(panel).findByPlaceholderText('Search help…')
    expect(within(panel).getAllByRole('group').length >= 0).toBe(true)
    await user.type(box, 'replay history')
    expect(within(panel).queryByText('Stop everything')).not.toBeInTheDocument()
    await user.click(within(panel).getByText('Know whether a replay worked'))
    expect(within(panel).getAllByRole('link', { name: 'Open Replay history ›' })[0]).toHaveAttribute('href', '/next/azure/replays')
  })

  it('opens straight on one answer from ?topic=', async () => {
    open('/next/azure?panel=help&topic=stop-all')
    const panel = await screen.findByRole('dialog')
    expect(await within(panel).findByText(/It stops every replay ServiceHub would do on its own/)).toBeVisible()
  })

  it('says so when nothing matches', async () => {
    const user = userEvent.setup()
    open('/next/azure?panel=help')
    await user.type(await screen.findByPlaceholderText('Search help…'), 'zzzzqqq')
    expect(await screen.findByText(/Nothing matches/)).toBeInTheDocument()
  })

  it('types no threshold: 4.2.0’s “10 verified at 95%” must come from the trust rules, never from copy', () => {
    for (const a of nextHelpAnswers) expect(`${a.question} ${a.answer}`, a.id).not.toMatch(/\b\d+\s*(verified|%)/i)
  })

  it('has no accessibility violations', async () => {
    open('/next/azure?panel=help')
    const panel = await screen.findByRole('dialog')
    await within(panel).findByPlaceholderText('Search help…')
    await expectNoAxeViolations(panel)
  })
})

describe('Search (unit F7)', () => {
  const c = [withQueue]
  it('finds connections, queues and subscriptions, built pages and actions — and ignores topics', () => {
    const r = searchNext('orders', c, nextHelpAnswers, 'azure', '/next/azure')
    expect(r.map((x) => x.title)).toContain('orders-sb')
    expect(r.map((x) => x.title)).toContain('orders-queue')
    expect(searchNext('payments', c, nextHelpAnswers, 'azure', '/next/azure').map((x) => x.title)).toEqual(['payments-topic / sub-1'])
    expect(searchNext('replay history', c, nextHelpAnswers, 'azure', '/next/azure').find((x) => x.group === 'Pages')?.href).toBe('/next/azure/replays')
    expect(searchNext('emergency', c, nextHelpAnswers, 'azure', '/next/azure/replays').find((x) => x.group === 'Do')?.href).toBe('/next/azure/replays?dialog=emergency-stop')
  })

  it('never offers a page that is not built, and never searches message contents', () => {
    expect(searchNext('connect a component', c, [], 'azure', '/next/azure').filter((x) => x.group === 'Do')).toEqual([]) // Connect is not built yet
    expect(searchNext('zzz-body-text', c, nextHelpAnswers, 'azure', '/next/azure')).toEqual([])
  })

  it('under all clouds, Home is the all-clouds page, not a page for a cloud called “all”', () => {
    const hrefs = searchNext('all clouds', c, [], 'all', '/next').map((x) => x.href)
    expect(searchNext('home', c, [], 'all', '/next').map((x) => x.href)).not.toContain('/next/all')
    expect(hrefs).toContain('/next')
  })

  it('opens on ⌘K and Ctrl-K from anywhere, finds a queue, and Enter goes there with arrow keys to choose', async () => {
    const user = userEvent.setup()
    const { router } = open('/next/azure')
    await screen.findByRole('heading', { level: 1, name: /Dashboard/ })
    await user.keyboard('{Control>}k{/Control}')
    const box = await screen.findByRole('combobox')
    await waitFor(() => expect(router.state.location.search).toBe('?dialog=search'))
    await user.type(box, 'orders')
    const options = await screen.findAllByRole('option')
    expect(options.length).toBeGreaterThan(1)
    await user.keyboard('{ArrowDown}{Enter}')
    await waitFor(() => expect(router.state.location.pathname).toBe('/next/azure/c/service-bus/ns-azure'))
    expect(router.state.location.search).toContain('tab=dead-letters&entity=orders-queue')
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument()) // navigating closed it
  })

  it('says it never searches inside message bodies, and what matches nothing', async () => {
    const user = userEvent.setup()
    open('/next/azure?dialog=search')
    expect(await screen.findByText(/never searches inside message bodies/)).toBeInTheDocument()
    await user.type(screen.getByRole('combobox'), 'zzzqqq')
    expect(await screen.findByText(/Nothing matches/)).toBeInTheDocument()
  })

  it('has no accessibility violations', async () => {
    open('/next/azure?dialog=search')
    const dialog = await screen.findByRole('dialog')
    await within(dialog).findByRole('combobox')
    await expectNoAxeViolations(dialog)
  })
})
