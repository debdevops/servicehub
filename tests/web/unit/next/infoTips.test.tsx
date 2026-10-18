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
import { labels } from '@/next/content/info'
import { agent, approval, component, coverageRows, handOff, me, page } from './homeData'

vi.mock('@/lib/api/coverage', async (o) => ({ ...(await o<typeof coverageApi>()), fetchCoverage: vi.fn() }))
vi.mock('@/lib/api/components', async (o) => ({ ...(await o<typeof componentsApi>()), fetchComponents: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn() }))
vi.mock('@/lib/api/pendingWork', async (o) => ({ ...(await o<typeof pendingApi>()), fetchPendingWork: vi.fn() }))

const at = '2026-10-18T10:00:00Z'
function open(u: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter([nextRoute], { initialEntries: [u] })
  return { router, ...render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>) }
}

beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
  vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [component()], generatedAtUtc: at })
  vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
  vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
  vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
  vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([approval(), handOff()]))
})

describe('the (i) beside everything', () => {
  it('opens on hover with the explanation, and closes when the pointer leaves', async () => {
    const user = userEvent.setup()
    open('/next/azure/attention')
    const table = await screen.findByRole('table')
    const tip = within(table).getByRole('button', { name: 'About Age' })
    await user.hover(tip)
    expect(await screen.findByRole('note')).toHaveTextContent(labels['Age'].text)
    await user.unhover(tip)
    await waitFor(() => expect(screen.queryByRole('note')).not.toBeInTheDocument())
  })

  it('opens on click and stays open until Esc, a click elsewhere, or the same button again', async () => {
    const user = userEvent.setup()
    open('/next/azure/attention')
    const tip = within(await screen.findByRole('table')).getByRole('button', { name: 'About Age' })
    await user.click(tip)
    expect(await screen.findByRole('note')).toHaveTextContent(labels['Age'].text)
    await user.unhover(tip)
    expect(screen.getByRole('note')).toBeInTheDocument() // a click pins it
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('note')).not.toBeInTheDocument())
  })

  it('opens from the keyboard (focus, then Enter) too', async () => {
    const user = userEvent.setup()
    open('/next/azure/attention')
    const tip = (await screen.findAllByRole('button', { name: 'About Needs your attention' }))[0]
    tip.focus()
    expect(await screen.findByRole('note')).toBeInTheDocument()
    await user.keyboard('{Enter}')
    expect(screen.getByRole('note')).toBeInTheDocument()
  })

  it('has one on every column of the Needs your attention table, and says what Type means THERE (not what Type means on a queue)', async () => {
    const user = userEvent.setup()
    open('/next/azure/attention')
    const table = await screen.findByRole('table')
    for (const th of within(table).getAllByRole('columnheader')) {
      if (!th.textContent?.trim()) continue
      expect(within(th).getAllByRole('button').length, th.textContent ?? '').toBeGreaterThan(0)
    }
    await user.hover(within(table).getByRole('button', { name: 'About Type' }))
    expect(await screen.findByRole('note')).toHaveTextContent(/An Approval is a replay the Agent prepared/)
  })

  it('has one on the page title, each tile, the header items and every menu link', async () => {
    open('/next/azure/attention')
    await screen.findByRole('heading', { level: 1, name: /Needs your attention/ })
    for (const name of ['About Approvals', 'About Hand-offs', 'About Oldest', 'About The Agent', 'About Emergency stop', 'About Help', 'About Search', 'About Your role', 'About Pending approvals', 'About Active messages', 'About Replay history', 'About Auto Replay rules', 'About Failure signatures', 'About Evidence ledger', 'About Agents', 'About Configuration', 'About What ServiceHub covers']) {
      expect((await screen.findAllByRole('button', { name })).length, name).toBeGreaterThan(0)
    }
  })

  it('has one on the filters, and keeps the filters’ own names clean for screen readers', async () => {
    open('/next/azure/attention')
    expect(await screen.findByLabelText('Family:')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'About Family filter' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'About Age filter' })).toBeInTheDocument()
  })

  it('has one in the dialogs too: the hand-off panel’s three choices', async () => {
    open('/next/azure?panel=hand-off&entry=e2')
    const dialog = await screen.findByRole('dialog')
    for (const name of ['About Replay it again', 'About Mark as settled', 'About Leave it']) expect(await within(dialog).findByRole('button', { name })).toBeInTheDocument()
  })
})
