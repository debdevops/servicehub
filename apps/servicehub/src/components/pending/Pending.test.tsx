import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as api from '../../lib/api/pendingWork'
import type { PendingWorkItem, PendingWorkPage } from '../../lib/api/pendingWork'
import { Bell } from './Bell'
import { EscalationToast } from './EscalationToast'
import { NeedsYouStrip } from './NeedsYouStrip'

vi.mock('../../lib/api/pendingWork', async (original) => ({ ...(await original<typeof api>()), fetchPendingWork: vi.fn() }))

const item = (id: string, over: Partial<PendingWorkItem> = {}): PendingWorkItem => ({
  kind: 'approval', id, entryId: id, agentId: null, dlqMessageId: 1, namespaceId: 'n1', namespaceName: 'aws-dev', provider: 'aws', environment: 'dev',
  entity: `queue-${id}`, deadLetterReason: 'Timeout', ruleId: 1, ruleName: 'Retry', reasonCode: 'PROVIDER_CANNOT_VERIFY_ABSENCE',
  reason: 'This cloud can’t prove a replayed message stayed fixed.', since: new Date().toISOString(), ...over,
})
const page = (items: PendingWorkItem[], byProvider: PendingWorkPage['byProvider'] = [{ provider: 'aws', count: items.length }]): PendingWorkPage => ({ items, total: items.length, byProvider, agents: 0 })

function wrap(ui: React.ReactNode, client = new QueryClient({ defaultOptions: { queries: { retry: false } } })) {
  return { client, ...render(<QueryClientProvider client={client}><MemoryRouter><Routes><Route path="*" element={ui} /></Routes></MemoryRouter></QueryClientProvider>) }
}

describe('the bell', () => {
  beforeEach(() => vi.clearAllMocks())

  it('counts pending work and opening it clears nothing — there is no read state', async () => {
    vi.mocked(api.fetchPendingWork).mockResolvedValue(page([item('a'), item('b')]))
    wrap(<Bell />)
    const bell = await screen.findByRole('button', { name: 'Waiting for you: 2' })
    await userEvent.click(bell)
    expect(await screen.findByText('2 replays need your approval')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Review' })).toHaveAttribute('href', expect.stringContaining('modal=approve'))
    await userEvent.click(bell)
    expect(screen.getByRole('button', { name: 'Waiting for you: 2' })).toBeInTheDocument()
  })

  it('says so when nothing is waiting', async () => {
    vi.mocked(api.fetchPendingWork).mockResolvedValue(page([]))
    wrap(<Bell />)
    expect(await screen.findByRole('button', { name: 'Nothing is waiting for you' })).toBeInTheDocument()
  })
})

describe('the toast', () => {
  beforeEach(() => vi.clearAllMocks())

  it('never toasts what was already waiting, and toasts a new item however the list learned of it', async () => {
    vi.mocked(api.fetchPendingWork).mockResolvedValue(page([item('old')]))
    const { client } = wrap(<EscalationToast />)
    await waitFor(() => expect(client.getQueryCache().getAll().some((q) => q.state.data !== undefined)).toBe(true))
    expect(screen.queryByText('The Agent stopped and asked you')).not.toBeInTheDocument()

    // A refetch — from a stream event or the poll; the toast does not care which.
    vi.mocked(api.fetchPendingWork).mockResolvedValue(page([item('old'), item('new')]))
    await act(async () => { await client.invalidateQueries() })
    expect(await screen.findByText('The Agent stopped and asked you')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Later' }))
    expect(screen.queryByText('The Agent stopped and asked you')).not.toBeInTheDocument()
  })
})

describe('Needs you', () => {
  beforeEach(() => vi.clearAllMocks())

  it('is one small sentence when nothing needs anyone — never a banner', async () => {
    vi.mocked(api.fetchPendingWork).mockResolvedValue(page([], []))
    wrap(<NeedsYouStrip provider="azure" watching="The Agent is watching 12 queues." />)
    expect(await screen.findByText('Nothing needs you. The Agent is watching 12 queues.')).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Needs you' })).not.toBeInTheDocument()
  })

  it('shows at most three rows and points to the bell for the rest; other clouds get one line', async () => {
    const rules = ['1', '2', '3', '4'].map((n) => item(`rule:${n}`, { kind: 'rule', entryId: null, ruleId: Number(n), ruleName: `Rule ${n}`, provider: 'azure', reasonCode: 'RULE_CIRCUIT_BREAKER' }))
    vi.mocked(api.fetchPendingWork).mockImplementation(async (scope) =>
      scope?.provider ? page(rules, [{ provider: 'azure', count: 4 }]) : page([...rules, item('x')], [{ provider: 'azure', count: 4 }, { provider: 'aws', count: 1 }]))
    wrap(<NeedsYouStrip provider="azure" watching="" />)
    expect(await screen.findAllByText('A rule stopped itself')).toHaveLength(3)
    expect(screen.getByText(/\+ 1 more/)).toBeInTheDocument()
    expect(await screen.findByText(/1 replay needs your approval in AWS/)).toBeInTheDocument()
  })
})
