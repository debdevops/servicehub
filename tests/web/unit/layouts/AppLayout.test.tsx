import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as namespacesApi from '@/lib/api/namespaces'
import type { CloudProvider, Namespace } from '@/lib/api/namespaces'
import { AppLayout } from '@/layouts/AppLayout'

vi.mock('@/lib/api/namespaces')
const mocked = vi.mocked(namespacesApi)

const ns = (provider: CloudProvider, n = 1): Namespace =>
  ({ id: `${provider}-${n}`, name: `${provider}-${n}`, provider, lastConnectionTestSucceeded: null }) as Namespace

function Where() {
  const { pathname, search } = useLocation()
  return <output data-testid="where">{pathname + search}</output>
}

function renderApp(initial = '/') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[initial]}>
        <Routes>
          <Route element={<AppLayout />}>
            <Route index element={<Where />} />
            <Route path="*" element={<Where />} />
          </Route>
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

const clouds = () => screen.getByRole('list', { name: 'Connected clouds' })
const where = () => screen.getByTestId('where').textContent

describe('AppLayout — the sidebar reflects what is connected', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    window.localStorage.clear()
    delete document.documentElement.dataset.provider
  })

  it('with nothing connected: no provider rows, no work, one way to add a cloud', async () => {
    mocked.fetchNamespaces.mockResolvedValue([])
    renderApp()

    await waitFor(() => expect(screen.queryByText('Loading your clouds…')).not.toBeInTheDocument())
    expect(within(clouds()).queryAllByRole('button')).toHaveLength(0)
    expect(screen.getByRole('link', { name: /Add a cloud/ })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /Dead letters/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /Fleet Overview/ })).not.toBeInTheDocument()
    expect(where()).toBe('/')
  })

  it('shows exactly the connected providers — never an unconnected one, greyed out or not', async () => {
    mocked.fetchNamespaces.mockResolvedValue([ns('azure', 1), ns('azure', 2)])
    renderApp()

    const rows = await within(await screen.findByRole('list', { name: 'Connected clouds' })).findAllByRole('button')
    expect(rows).toHaveLength(1)
    expect(rows[0]).toHaveTextContent('Azure')
    expect(rows[0]).toHaveTextContent('2 namespaces')
    expect(screen.queryByText(/AWS/)).not.toBeInTheDocument()
    expect(screen.queryByText(/Google Cloud/)).not.toBeInTheDocument()
    expect(screen.queryByText(/not configured/i)).not.toBeInTheDocument()
  })

  it('lands on Home for one cloud', async () => {
    mocked.fetchNamespaces.mockResolvedValue([ns('azure')])
    renderApp()

    await screen.findByRole('link', { name: /Dead letters/ })
    expect(where()).toBe('/')
    expect(screen.queryByRole('link', { name: /Fleet Overview/ })).not.toBeInTheDocument()
  })

  it('lands on Home for two clouds too — Fleet Overview is a section of it now, not a second page', async () => {
    mocked.fetchNamespaces.mockResolvedValue([ns('azure'), ns('aws')])
    renderApp()

    await waitFor(() => expect(where()).toBe('/'))
    expect(screen.queryByRole('link', { name: /Fleet Overview/ })).not.toBeInTheDocument()
  })

  it('leaves a deep link where it points', async () => {
    mocked.fetchNamespaces.mockResolvedValue([ns('azure'), ns('aws')])
    renderApp('/?tab=dlq')

    await screen.findByRole('link', { name: /Dead letters/ })
    expect(where()).toBe('/?tab=dlq')
  })

  it('lights exactly one of Replayed and Auto Replay when the rules panel is open over the Replayed tab', async () => {
    mocked.fetchNamespaces.mockResolvedValue([ns('azure')])
    renderApp('/?tab=replayed&panel=rules')

    const auto = await screen.findByRole('link', { name: /Auto Replay/ })
    expect(auto).toHaveAttribute('aria-current', 'page')
    expect(screen.getByRole('link', { name: /Replayed/ })).not.toHaveAttribute('aria-current')
  })

  it.each([
    '/', '/?tab=dlq', '/?tab=active', '/?tab=replayed', '/?tab=replayed&panel=rules', '/?tab=dlq&panel=rules', '/?panel=rules', '/?tab=replayed&panel=connections',
    '/?tab=dlq&panel=help', '/?tab=replayed&modal=settings', '/?tab=dlq&modal=bulk-replay', '/?tab=active&modal=approve', '/?tab=nonsense',
    '/advanced', '/advanced/ledger', '/advanced/ledger?state=Declined', '/advanced/signatures?tab=growing', '/advanced/agents?agent=auto-replay',
  ])('lights exactly one place in the sidebar for %s', async (url) => {
    mocked.fetchNamespaces.mockResolvedValue([ns('azure')])
    renderApp(url)

    await screen.findByRole('link', { name: /Dead letters|Advanced Overview/ })
    const nav = screen.getAllByRole('navigation').find((n) => within(n).queryAllByRole('link').length > 3) ?? document.body
    const lit = within(nav).queryAllByRole('link').filter((a) => a.getAttribute('aria-current') === 'page').map((a) => a.textContent?.trim())
    expect(lit, `lit for ${url}: ${lit.join(' | ')}`).toHaveLength(1)
  }, 20_000)

  it('opening Settings or Help from the sidebar puts a message drawer away, so one click opens one window', async () => {
    mocked.fetchNamespaces.mockResolvedValue([ns('azure')])
    renderApp('/?tab=dlq&message=5&view=full&active=9')

    const settings = await screen.findByRole('link', { name: /Settings/ })
    const href = settings.getAttribute('href')!
    expect(href).toContain('modal=settings')
    expect(href).toContain('tab=dlq')
    for (const k of ['message=', 'view=', 'active=']) expect(href).not.toContain(k)
  })

  it('remembers the chosen cloud and tints only the accent', async () => {
    mocked.fetchNamespaces.mockResolvedValue([ns('azure'), ns('aws')])
    renderApp('/?ns=azure-1')

    const aws = await within(await screen.findByRole('list', { name: 'Connected clouds' })).findByRole('button', { name: /AWS/ })
    expect(aws).toHaveAttribute('aria-pressed', 'false')

    await userEvent.click(aws)

    expect(aws).toHaveAttribute('aria-pressed', 'true')
    expect(window.localStorage.getItem('servicehub.provider')).toBe('aws')
    expect(document.documentElement.dataset.provider).toBe('aws')
    // D48: choosing a cloud from Home sets `?provider=` — the same scope a cloud card or a scope tab
    // sets — never a bare `/`, which would mean "All clouds".
    expect(where()).toBe('/?provider=aws')
  })

  it('restores the remembered cloud on the next visit', async () => {
    window.localStorage.setItem('servicehub.provider', 'aws')
    mocked.fetchNamespaces.mockResolvedValue([ns('azure'), ns('aws')])
    renderApp('/?tab=dlq')

    const aws = await within(await screen.findByRole('list', { name: 'Connected clouds' })).findByRole('button', { name: /AWS/ })
    expect(aws).toHaveAttribute('aria-pressed', 'true')
  })

  it('ignores a remembered cloud that is no longer connected', async () => {
    window.localStorage.setItem('servicehub.provider', 'gcp')
    mocked.fetchNamespaces.mockResolvedValue([ns('azure')])
    renderApp()

    const azure = await within(await screen.findByRole('list', { name: 'Connected clouds' })).findByRole('button', { name: /Azure/ })
    expect(azure).toHaveAttribute('aria-pressed', 'true')
    expect(document.documentElement.dataset.provider).toBeUndefined()
  })

  it('still works when storage is blocked', async () => {
    const setItem = vi.spyOn(window.localStorage, 'setItem').mockImplementation(() => {
      throw new Error('blocked')
    })
    mocked.fetchNamespaces.mockResolvedValue([ns('azure'), ns('aws')])
    renderApp('/?tab=dlq')

    const aws = await within(await screen.findByRole('list', { name: 'Connected clouds' })).findByRole('button', { name: /AWS/ })
    await userEvent.click(aws)
    expect(aws).toHaveAttribute('aria-pressed', 'true')
    setItem.mockRestore()
  })

  it('says plainly when the clouds cannot be loaded, and lets you retry', async () => {
    mocked.fetchNamespaces.mockRejectedValueOnce(new Error('AxiosError: 500 at /api/v1/namespaces'))
    renderApp()

    expect(await screen.findByText('Couldn’t load your clouds.')).toBeInTheDocument()
    expect(screen.queryByText(/AxiosError|500/)).not.toBeInTheDocument()

    mocked.fetchNamespaces.mockResolvedValue([ns('azure')])
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }))
    await within(await screen.findByRole('list', { name: 'Connected clouds' })).findByRole('button', { name: /Azure/ })
  })

  it('keeps the tab you are on when a panel opens over it', async () => {
    mocked.fetchNamespaces.mockResolvedValue([ns('azure')])
    renderApp('/?tab=dlq')

    await userEvent.click(await screen.findByRole('link', { name: /Help/ }))
    expect(where()).toBe('/?tab=dlq&panel=help')
    expect(clouds()).toBeInTheDocument()
  })
})

describe('AppLayout — the menu on a narrow screen', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    window.localStorage.clear()
  })

  it('opens and closes the sidebar from a Menu button, and Esc closes it', async () => {
    mocked.fetchNamespaces.mockResolvedValue([ns('azure', 1)])
    renderApp()
    const user = userEvent.setup()
    const menu = await screen.findByRole('button', { name: 'Menu' })
    expect(menu).toHaveAttribute('aria-expanded', 'false')
    expect(menu).toHaveAttribute('aria-controls', 'main-nav')

    await user.click(menu)
    expect(menu).toHaveAttribute('aria-expanded', 'true')
    await user.keyboard('{Escape}')
    expect(menu).toHaveAttribute('aria-expanded', 'false')
  })
})
