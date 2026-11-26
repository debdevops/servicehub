import { act, render, screen, waitFor } from '@testing-library/react'
import { createMemoryRouter, RouterProvider, useLocation } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import * as demoState from '@/lib/demo/state'
import { BotLauncher } from '@/next/components/BotLauncher'

vi.mock('@/lib/demo/state', async (o) => ({ ...(await o<typeof demoState>()), isDemo: vi.fn(() => false) }))

function Where() {
  const at = useLocation()
  return <output data-testid="where">{`${at.pathname}${at.search}`}</output>
}

function mount(enabled: boolean, start = '/azure') {
  const router = createMemoryRouter(
    [{ path: '*', element: <><BotLauncher enabled={enabled} /><Where /></> }],
    { initialEntries: [start] },
  )
  return render(<RouterProvider router={router} />)
}

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json; charset=utf-8' } })
const bot = () => document.querySelector('servicehub-bot')
const scripts = () => document.querySelectorAll('script[data-servicehub-bot]')

describe('ServiceHub Bot launcher', () => {
  const fetchMock = vi.fn<typeof fetch>()

  beforeEach(() => {
    vi.mocked(demoState.isDemo).mockReturnValue(false)
    fetchMock.mockReset()
    vi.stubGlobal('fetch', fetchMock)
  })
  afterEach(() => {
    vi.unstubAllGlobals()
    document.querySelectorAll('servicehub-bot, script[data-servicehub-bot]').forEach((el) => el.remove())
  })

  it('does nothing and asks nothing when run.sh did not start the bot (Docker, the public demo, the test suites)', async () => {
    mount(false)
    await act(async () => {})
    expect(fetchMock).not.toHaveBeenCalled()
    expect(bot()).toBeNull()
    expect(scripts()).toHaveLength(0)
  })

  it('adds the button when the bot answers its health check, reading live numbers in the real app', async () => {
    fetchMock.mockResolvedValue(json({ ok: true, knowledgeBase: {} }))
    mount(true)
    await waitFor(() => expect(bot()).not.toBeNull())
    expect(fetchMock).toHaveBeenCalledWith('/bot/health', expect.objectContaining({ headers: { accept: 'application/json' } }))
    expect(bot()?.getAttribute('endpoint')).toBe('/bot/ask')
    expect(bot()?.getAttribute('live')).toBe('true')
    expect(bot()?.hasAttribute('app-base')).toBe(false)
    expect(scripts()).toHaveLength(1)
    expect(scripts()[0]?.getAttribute('src')).toBe('/bot/widget.js')
    expect(scripts()[0]?.getAttribute('type')).toBe('module')
  })

  it('in the demo, answers from the knowledge base only (no API behind it) and keeps links inside /demo', async () => {
    vi.mocked(demoState.isDemo).mockReturnValue(true)
    fetchMock.mockResolvedValue(json({ ok: true }))
    mount(true)
    await waitFor(() => expect(bot()).not.toBeNull())
    expect(bot()?.getAttribute('live')).toBe('false')
    expect(bot()?.getAttribute('app-base')).toBe('/demo')
  })

  it.each([
    ['an HTML page (a server that is not the bot)', () => new Response('<!doctype html>', { status: 200, headers: { 'content-type': 'text/html' } })],
    ['a proxy error', () => new Response('Bad gateway', { status: 502 })],
    ['JSON that is not the bot', () => json({ status: 'Healthy' })],
  ])('shows nothing when /bot/health returns %s', async (_name, response) => {
    fetchMock.mockResolvedValue(response())
    mount(true)
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    await act(async () => {})
    expect(bot()).toBeNull()
    expect(scripts()).toHaveLength(0)
  })

  it('shows nothing when the bot cannot be reached', async () => {
    fetchMock.mockRejectedValue(new TypeError('fetch failed'))
    mount(true)
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    await act(async () => {})
    expect(bot()).toBeNull()
  })

  it('loads the widget script once and takes the button away when the layout goes', async () => {
    fetchMock.mockImplementation(async () => json({ ok: true }))
    const first = mount(true)
    await waitFor(() => expect(bot()).not.toBeNull())
    first.unmount()
    expect(bot()).toBeNull()
    mount(true)
    await waitFor(() => expect(bot()).not.toBeNull())
    expect(scripts()).toHaveLength(1)
    expect(document.querySelectorAll('servicehub-bot')).toHaveLength(1)
  })

  it('moves inside the app when a bot link is followed, without a page load', async () => {
    fetchMock.mockResolvedValue(json({ ok: true }))
    mount(true, '/azure')
    await waitFor(() => expect(bot()).not.toBeNull())

    const go = (href: string) => {
      const event = new CustomEvent('servicehub-bot:navigate', { detail: { href }, cancelable: true, bubbles: true, composed: true })
      act(() => void bot()?.dispatchEvent(event))
      return event.defaultPrevented
    }
    expect(go('/azure/c/service-bus/n1?tab=dead-letters&reason=X')).toBe(true)
    expect(screen.getByTestId('where').textContent).toBe('/azure/c/service-bus/n1?tab=dead-letters&reason=X')
    expect(go('?panel=help')).toBe(true)
    expect(screen.getByTestId('where').textContent).toBe('/azure/c/service-bus/n1?panel=help')
  })

  it('in the demo, drops /demo before handing the address to the router (it is the basename)', async () => {
    vi.mocked(demoState.isDemo).mockReturnValue(true)
    fetchMock.mockResolvedValue(json({ ok: true }))
    mount(true, '/aws')
    await waitFor(() => expect(bot()).not.toBeNull())
    act(() => void bot()?.dispatchEvent(new CustomEvent('servicehub-bot:navigate', { detail: { href: '/demo/aws/replays' }, cancelable: true })))
    expect(screen.getByTestId('where').textContent).toBe('/aws/replays')
  })
})
