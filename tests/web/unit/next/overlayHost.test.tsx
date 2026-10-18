import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as agentsApi from '@/lib/api/agents'
import * as componentsApi from '@/lib/api/components'
import * as coverageApi from '@/lib/api/coverage'
import * as identityApi from '@/lib/api/identity'
import * as settingsApi from '@/lib/api/settings'
import { nextNavigation, type NextOverlay } from '@/next/nav/navigation'
import { nextOverlayBodies, nextOverlayCompanions } from '@/next/overlays/registry'
import { nextRoute } from '@/next/router'
import { agent, coverageRows, me } from './homeData'

vi.mock('@/lib/api/coverage', async (o) => ({ ...(await o<typeof coverageApi>()), fetchCoverage: vi.fn() }))
vi.mock('@/lib/api/components', async (o) => ({ ...(await o<typeof componentsApi>()), fetchComponents: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn() }))

const overlays = nextNavigation.filter((e): e is NextOverlay => e.kind !== 'page')

function open(url: string) {
  const router = createMemoryRouter([nextRoute], { initialEntries: [url] })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>)
  return router
}

describe('the 5.0.0 overlay host (unit F1, host half)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [], generatedAtUtc: '2026-10-18T10:00:00Z' })
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
  })

  it('every registered body names an entry in the navigation array, so one cannot be registered under a typo', () => {
    const ids = new Set(overlays.map((e) => e.id))
    for (const id of Object.keys(nextOverlayBodies)) expect(ids.has(id), id).toBe(true)
  })

  it('every overlay in the array opens a frame: its body when built, a plain "not built yet" when not, never nothing', async () => {
    for (const entry of overlays.filter((e) => !(e.id in nextOverlayBodies))) {
      const { unmount } = (() => { const router = open(`/next/azure?${entry.kind}=${entry.value}`); return { unmount: () => router.dispose() } })()
      expect(await screen.findByText(`Not built yet — unit ${entry.unit}`), entry.id).toBeInTheDocument()
      expect(screen.getByText(entry.design)).toBeInTheDocument()
      unmount()
      document.body.innerHTML = ''
    }
  })

  it('opens nothing for a value that names no overlay, and nothing without a parameter', async () => {
    open('/next/coverage?dialog=nonsense')
    await screen.findByRole('heading', { level: 1, name: 'What ServiceHub covers' })
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('a dialog is not a panel: a value only opens the kind it belongs to', async () => {
    open('/next/coverage?panel=message') // `message` is a dialog
    await screen.findByRole('heading', { level: 1, name: 'What ServiceHub covers' })
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('Esc closes it and removes its parameters, keeping the page’s own (?tab=)', async () => {
    const router = open('/next/coverage?tab=x&dialog=emergency-stop&id=7&entry=e1')
    expect(await screen.findByText(/Not built yet — unit F5/)).toBeInTheDocument()
    await userEvent.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByText(/Not built yet — unit F5/)).not.toBeInTheDocument())
    expect(router.state.location.search).toBe('?tab=x')
  })

  it('the Close button closes it, and closing replaces the entry so Back does not reopen it', async () => {
    const router = open('/next/coverage')
    await screen.findByRole('heading', { level: 1, name: 'What ServiceHub covers' })
    await router.navigate('/next/coverage?panel=help')
    expect(await screen.findByText(/Not built yet — unit F6/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Close' }))
    await waitFor(() => expect(screen.queryByText(/Not built yet — unit F6/)).not.toBeInTheDocument())
    await router.navigate(-1)
    expect(router.state.location.search).toBe('') // back went to the page before the dialog, not to a reopened one
  })

  it('Back closes a dialog that was opened by a link', async () => {
    const router = open('/next/coverage')
    await screen.findByRole('heading', { level: 1, name: 'What ServiceHub covers' })
    await router.navigate('/next/coverage?dialog=send')
    expect(await screen.findByText(/Not built yet — unit F8/)).toBeInTheDocument()
    await router.navigate(-1)
    await waitFor(() => expect(screen.queryByText(/Not built yet — unit F8/)).not.toBeInTheDocument())
  })

  it('a panel and a dialog can be open together, as the old host allowed', async () => {
    open('/next/coverage?panel=help&dialog=send')
    expect(await screen.findByText(/Not built yet — unit F6/)).toBeInTheDocument()
    expect(await screen.findByText(/Not built yet — unit F8/)).toBeInTheDocument()
  })

  it('knows the parameters it owns', () => {
    expect(nextOverlayCompanions).toEqual(expect.arrayContaining(['id', 'entry']))
    expect(nextOverlayCompanions).not.toContain('tab')
    expect(nextOverlayCompanions).not.toContain('entity')
  })
})
