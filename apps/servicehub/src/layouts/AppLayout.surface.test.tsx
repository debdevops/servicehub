import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as namespacesApi from '../lib/api/namespaces'
import type { CloudProvider, Namespace } from '../lib/api/namespaces'
import { AppLayout } from './AppLayout'

vi.mock('../lib/api/namespaces')
const mocked = vi.mocked(namespacesApi)

const ns = (provider: CloudProvider): Namespace => ({ id: provider, name: provider, provider }) as Namespace

function Where() {
  const { pathname, search } = useLocation()
  return <output data-testid="where">{pathname + search}</output>
}

function renderApp(initial: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[initial]}>
        <Routes>
          <Route element={<AppLayout />}>
            {['/', '/fleet', '/advanced', '/advanced/ledger', '/advanced/agents', '/advanced/signatures'].map((path) => (
              <Route key={path} path={path} element={<Where />} />
            ))}
          </Route>
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

const sidebar = () => within(screen.getByRole('navigation', { name: 'Main' }))
const where = () => screen.getByTestId('where').textContent
const switchLink = (name: 'Simple' | 'Advanced') => within(screen.getByRole('navigation', { name: 'Surface' })).getByRole('link', { name })

describe('the Simple | Advanced switch', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    window.localStorage.clear()
    mocked.fetchNamespaces.mockResolvedValue([ns('azure')])
  })

  it('shows only Simple’s entries on Simple and only Advanced’s on Advanced', async () => {
    const first = renderApp('/')
    expect(await sidebar().findByRole('link', { name: /Dead letters/ })).toBeInTheDocument()
    expect(sidebar().queryByRole('link', { name: /Recovery Ledger/ })).not.toBeInTheDocument()
    expect(switchLink('Simple')).toHaveAttribute('aria-current', 'page')
    first.unmount()

    renderApp('/advanced/ledger')
    expect(await sidebar().findByRole('link', { name: /Recovery Ledger/ })).toBeInTheDocument()
    expect(sidebar().getByRole('link', { name: /Failure Signatures/ })).toBeInTheDocument()
    expect(sidebar().queryByRole('link', { name: /Dead letters/ })).not.toBeInTheDocument()
    expect(sidebar().queryByText('Clouds')).not.toBeInTheDocument()
    expect(switchLink('Advanced')).toHaveAttribute('aria-current', 'page')
  })

  it('highlights exactly one sidebar row on an Advanced page — the Overview root must not match every page beneath it', async () => {
    renderApp('/advanced/signatures')
    await sidebar().findByRole('link', { name: /Failure Signatures/ })

    const current = sidebar().getAllByRole('link').filter((l) => l.getAttribute('aria-current') === 'page')
    expect(current.map((l) => l.textContent)).toEqual(['Failure Signatures'])
  })

  it('always starts a first visit on Simple, and the Advanced link goes to the top of Advanced', async () => {
    renderApp('/')
    await screen.findByRole('link', { name: /Dead letters/ })
    expect(where()).toBe('/')
    expect(switchLink('Advanced')).toHaveAttribute('href', '/advanced')
  })

  it('goes Simple → Advanced → Simple by the switch, and Advanced remembers where you were', async () => {
    renderApp('/advanced/agents')
    await sidebar().findByRole('link', { name: /Agents/ })

    await userEvent.click(switchLink('Simple'))
    expect(where()).toBe('/')
    expect(await sidebar().findByRole('link', { name: /Dead letters/ })).toBeInTheDocument()

    expect(switchLink('Advanced')).toHaveAttribute('href', '/advanced/agents')
    await userEvent.click(switchLink('Advanced'))
    expect(where()).toBe('/advanced/agents')
    expect(sidebar().queryByRole('link', { name: /Dead letters/ })).not.toBeInTheDocument()
  })

  it('sends Simple to Fleet Overview when two clouds are connected, as a landing does', async () => {
    mocked.fetchNamespaces.mockResolvedValue([ns('azure'), ns('aws')])
    renderApp('/advanced')
    await sidebar().findByRole('link', { name: /Agents/ })
    await waitFor(() => expect(switchLink('Simple')).toHaveAttribute('href', '/fleet'))
  })

  it('agrees with a pasted URL: the sidebar follows the path alone', async () => {
    renderApp('/advanced/signatures')
    expect(await sidebar().findByRole('link', { name: /Recovery Ledger/ })).toBeInTheDocument()
    expect(document.querySelector('[data-surface]')).toHaveAttribute('data-surface', 'advanced')
  })

  it('ignores a remembered path that is not an Advanced page', async () => {
    window.localStorage.setItem('servicehub.advanced.last', '/fleet')
    renderApp('/')
    await screen.findByRole('link', { name: /Dead letters/ })
    expect(switchLink('Advanced')).toHaveAttribute('href', '/advanced')
  })

  it('works identically with storage unavailable', async () => {
    vi.spyOn(window.localStorage, 'getItem').mockImplementation(() => {
      throw new Error('blocked')
    })
    vi.spyOn(window.localStorage, 'setItem').mockImplementation(() => {
      throw new Error('blocked')
    })
    renderApp('/advanced/ledger')
    await sidebar().findByRole('link', { name: /Recovery Ledger/ })
    await userEvent.click(switchLink('Simple'))
    expect(where()).toBe('/')
    expect(switchLink('Advanced')).toHaveAttribute('href', '/advanced')
    vi.restoreAllMocks()
  })
})
