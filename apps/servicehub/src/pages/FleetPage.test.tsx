import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as fleetApi from '../lib/api/fleet'
import * as api from '../lib/api/namespaces'
import type { Namespace } from '../lib/api/namespaces'
import { FleetPage } from './FleetPage'
import { expectNoAxeViolations } from '../test/axe'

vi.mock('../lib/api/fleet')
vi.mock('../lib/api/namespaces')

const ns = (id: string, provider: 'azure' | 'aws' | 'gcp'): Namespace => ({ id, name: id, displayName: id, provider, lastConnectionTestSucceeded: true }) as Namespace

function renderFleet() {
  return render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter><FleetPage /></MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('Fleet Overview', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    window.localStorage.clear()
    vi.mocked(api.fetchNamespaces).mockResolvedValue([ns('a1', 'azure'), ns('w1', 'aws')])
    vi.mocked(api.fetchNamespaceStats).mockImplementation(async (id) => ({
      namespaceId: id, entities: [], activeMessages: 0, deadLetterMessages: id === 'w1' ? 10 : 12, messageCountsSupported: true, observedAt: 'now',
    }))
    vi.mocked(fleetApi.fetchFleet).mockResolvedValue({
      window: 'today', since: 'x',
      clouds: [
        { provider: 'azure', namespaceCount: 1, capability: 'canConfirm', watched: true, active: 12, newInWindow: 12, resolvedInWindow: 1 },
        { provider: 'aws', namespaceCount: 1, capability: 'observerRequired', watched: false, active: null, newInWindow: 0, resolvedInWindow: 0 },
      ],
      namespaces: [
        { id: 'w1', name: 'w1', displayName: 'aws-dev', provider: 'aws', environment: 'dev', watched: false, active: null, newInWindow: 0, resolvedInWindow: 0, topFailure: null, health: 'cannotTell' },
        { id: 'a1', name: 'a1', displayName: 'orders-dev', provider: 'azure', environment: 'dev', watched: true, active: 12, newInWindow: 12, resolvedInWindow: 1, topFailure: { reason: 'Timeout', count: 12 }, health: 'needsALook' },
      ],
      topFailures: [{ provider: 'azure', environment: 'dev', reason: 'Timeout', count: 12 }],
    })
  })

  it('has no accessibility violations (6.6)', async () => {
    renderFleet()
    await new Promise((r) => setTimeout(r, 150))
    await expectNoAxeViolations(document.body)
  })

  it('shows two clouds with two different capability states, each on its own numbers', async () => {
    renderFleet()
    const azure = await screen.findByRole('article', { name: 'Azure' })
    const aws = screen.getByRole('article', { name: 'AWS' })

    expect(within(azure).getByText('Can confirm a fix held')).toBeInTheDocument()
    expect(within(aws).getByText('Can’t confirm fixes yet')).toBeInTheDocument()
    expect(within(azure).getByText('12')).toBeInTheDocument()
    expect(within(aws).getByText('10')).toBeInTheDocument()
    expect(screen.queryByText('22')).not.toBeInTheDocument() // never a blended total
  })

  it('says "not watched" rather than showing zero for a cloud ServiceHub does not look at', async () => {
    renderFleet()
    const aws = await screen.findByRole('article', { name: 'AWS' })

    expect(within(aws).getAllByText('not watched')).toHaveLength(2)
  })

  it('offers to add the cloud that is not connected', async () => {
    renderFleet()
    expect(await screen.findByText('Google Cloud isn’t connected.')).toBeInTheDocument()
  })

  it('lists namespaces worst first and never calls an unwatched cloud healthy', async () => {
    renderFleet()
    const table = await screen.findByRole('table', { name: 'Namespaces, worst first' })

    expect(within(table).getByText('Can\'t tell')).toBeInTheDocument()
    expect(within(table).queryByText('Healthy')).not.toBeInTheDocument()
  })
})
