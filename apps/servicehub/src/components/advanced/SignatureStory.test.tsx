import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import * as api from '../../lib/api/signatures'
import { IncidentTimeline, TraceView } from './SignatureStory'

vi.mock('../../lib/api/signatures')

const wrap = (ui: React.ReactNode) => render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><MemoryRouter>{ui}</MemoryRouter></QueryClientProvider>)

describe('Incident and Trace', () => {
  it('tells a signature’s story newest first, each replay linking to its evidence', async () => {
    vi.mocked(api.fetchIncident).mockResolvedValue({ signatureHash: 'h', provider: 'azure', reason: 'Timeout', firstSeenAt: '2026-09-24T10:00:00Z', lastSeenAt: '2026-09-26T10:00:00Z', messages: 5, stillStuck: 2, replays: 1, stayedFixed: 1, cameBackAfterReplay: 0, timeline: [
      { at: '2026-09-26T10:00:00Z', kind: 'replayed', text: 'Replayed one from orders — stayed fixed.', entryId: 'e1', dlqMessageId: 3 },
      { at: '2026-09-24T10:00:00Z', kind: 'first_seen', text: 'First seen dead-lettered on orders in dev.', entryId: null, dlqMessageId: 1 },
    ] })
    wrap(<IncidentTimeline hash="h" provider="azure" />)
    const items = within(await screen.findByRole('list', { name: 'Incident timeline' })).getAllByRole('listitem')
    expect(items[0]).toHaveTextContent('Replayed one')
    expect(within(items[0]).getByRole('link', { name: /Evidence/ })).toHaveAttribute('href', '/advanced/ledger?entry=e1&window=all')
    expect(items[1]).toHaveTextContent('First seen')
  })

  it('traces a correlation id across clouds and says it never looks into a cloud to do it', async () => {
    const user = userEvent.setup()
    vi.mocked(api.fetchTrace).mockResolvedValue({ correlationId: 'order-42', clouds: ['azure', 'aws'], note: 'Only what ServiceHub recorded.', hops: [
      { at: '2026-09-26T08:00:00Z', kind: 'dead_lettered', place: { provider: 'azure', namespaceName: 'orders-dev', environment: 'dev' }, entity: 'orders', dlqMessageId: 1, namespaceId: 'a', detail: 'Timeout', entryId: null },
      { at: '2026-09-26T09:00:00Z', kind: 'dead_lettered', place: { provider: 'aws', namespaceName: 'sqs', environment: 'dev' }, entity: 'payments', dlqMessageId: 2, namespaceId: 'w', detail: null, entryId: null },
    ] })
    wrap(<TraceView />)
    expect(screen.getByText(/never looks into a cloud/)).toBeInTheDocument()
    await user.type(screen.getByPlaceholderText(/correlation id/), 'order-42')
    await user.click(screen.getByRole('button', { name: 'Trace' }))
    expect(api.fetchTrace).toHaveBeenCalledWith('order-42')
    const hops = within(await screen.findByRole('list', { name: 'Trace' })).getAllByRole('listitem')
    expect(hops.map((h) => within(h).getByText(/Azure|AWS/).textContent)).toEqual(['Azure', 'AWS'])
  })
})
