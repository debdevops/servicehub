import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as nsApi from '../../lib/api/namespaces'
import * as api from '../../lib/api/signatures'
import FailureSignaturesPage from './FailureSignaturesPage'
import { expectNoAxeViolations } from '../../test/axe'

vi.mock('../../lib/api/signatures')
vi.mock('../../lib/api/namespaces')

const sig = (over: Partial<api.Signature> = {}): api.Signature => ({
  signatureHash: 'h', provider: 'azure', reason: 'Validation', exampleError: 'Required field customerId missing', entities: ['payments-dlq'], messages: 47, activeNow: 47,
  firstSeenAt: new Date(Date.now() - 6 * 86400_000).toISOString(), lastSeenAt: new Date().toISOString(), daily: [1, 2, 3, 5, 8, 13, 15], growing: true,
  replays: { replayed: 3, stayedFixed: 0, returned: 3, unverified: 0 }, replayVerdict: 'doesnt',
  namespaces: [{ id: 'n1', name: 'payments-prod', displayName: null, environment: 'prod', messages: 47 }], ...over,
})

const page = (items: api.Signature[]): api.SignaturePage => ({ items, total: items.length, page: 1, pageSize: 25, all: items.length, growing: 1, replayHelps: 0, replayDoesNotHelp: 1 })

function renderPage(initial = '/advanced/signatures') {
  return render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter initialEntries={[initial]}><FailureSignaturesPage /></MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('Failure Signatures', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    window.localStorage.clear()
    vi.mocked(nsApi.fetchNamespaces).mockResolvedValue([
      { id: 'p1', name: 'orders-prod', displayName: 'Orders Prod', provider: 'azure', environment: 'prod' },
      { id: 'd1', name: 'orders-dev', displayName: 'Orders Dev', provider: 'azure', environment: 'dev' },
      { id: 'w1', name: 'sqs', displayName: 'AWS Dev', provider: 'aws', environment: 'dev' },
    ] as never)
  })

  it('has no accessibility violations (6.6)', async () => {
    renderPage()
    await new Promise((r) => setTimeout(r, 150))
    await expectNoAxeViolations(document.body)
  })

  it('counts over one environment or namespace when asked, and offers the choice across every cloud', async () => {
    vi.mocked(api.fetchSignatures).mockResolvedValue(page([sig()]))
    renderPage('/advanced/signatures?env=prod')

    await screen.findByRole('table', { name: 'Failure signatures' })
    expect(api.fetchSignatures).toHaveBeenLastCalledWith(expect.objectContaining({ environment: 'prod', namespaceId: undefined }))
    await userEvent.click(await screen.findByRole('button', { name: 'Namespace' }))
    await userEvent.click(screen.getByRole('option', { name: /Orders Dev/ }))
    await screen.findByRole('table', { name: 'Failure signatures' })
    expect(api.fetchSignatures).toHaveBeenLastCalledWith(expect.objectContaining({ namespaceId: 'd1', environment: undefined }))
  })

  it('leads with the failure in words, then messages and how replaying went, with tab counts', async () => {
    vi.mocked(api.fetchSignatures).mockResolvedValue(page([sig()]))
    renderPage()

    const table = await screen.findByRole('table', { name: 'Failure signatures' })
    expect(within(table).getByText('Required field customerId missing')).toBeInTheDocument()
    expect(within(table).getByText('0 of 3 stayed fixed')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Growing/ })).toHaveTextContent('1')
  })

  it('says "not replayed" rather than inventing an outcome, and shows no autonomy level', async () => {
    vi.mocked(api.fetchSignatures).mockResolvedValue(page([sig({ replays: { replayed: 0, stayedFixed: 0, returned: 0, unverified: 0 }, replayVerdict: 'unknown', growing: false })]))
    renderPage()

    expect(await screen.findByText('not replayed')).toBeInTheDocument()
    expect(screen.queryByText(/May it act/i)).not.toBeInTheDocument()
    expect(screen.queryByText(/Runs on its own/i)).not.toBeInTheDocument()
  })

  it('opens the detail in the same page, and only links to Auto Replay — it never creates a rule', async () => {
    vi.mocked(api.fetchSignatures).mockResolvedValue(page([sig()]))
    renderPage()

    await userEvent.click(await screen.findByRole('row', { name: /Required field customerId missing/ }))

    const detail = await screen.findByRole('complementary', { name: 'Signature details' })
    expect(within(detail).getByText(/Growing\./)).toBeInTheDocument()
    expect(within(detail).getByText(/No — 0 of 3 replays stayed fixed/)).toBeInTheDocument()
    expect(within(detail).getByText('payments-prod')).toBeInTheDocument()
    expect(within(detail).getByText('Production')).toBeInTheDocument()
    expect(within(detail).getByRole('button', { name: /Create an auto-replay rule from this/ })).toBeInTheDocument()
    expect(within(detail).queryByRole('button', { name: /^Create rule$/ })).not.toBeInTheDocument()
  })

  it('does not call it helping when nothing was verified', async () => {
    vi.mocked(api.fetchSignatures).mockResolvedValue(page([sig({ replays: { replayed: 2, stayedFixed: 0, returned: 0, unverified: 2 }, replayVerdict: 'unknown', growing: false })]))
    renderPage('/advanced/signatures?signature=azure:h')

    expect(await screen.findByText(/2 replayed, but none could be verified/)).toBeInTheDocument()
  })
})
