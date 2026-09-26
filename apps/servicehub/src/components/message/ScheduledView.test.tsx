import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import * as api from '../../lib/api/messages'
import { ScheduledView } from './ScheduledView'

vi.mock('../../lib/api/messages')

describe('Scheduled', () => {
  it('lists what is due later, soonest first, and says when the list stopped at its cap', async () => {
    vi.mocked(api.fetchScheduled).mockResolvedValue({ entity: 'orders', subscription: null, capped: true, messages: [
      { messageId: 'soon', sequenceNumber: 1, scheduledFor: '2026-09-26T12:00:00Z', sizeInBytes: 2, contentType: null, bodyPreview: '{}' },
      { messageId: 'later', sequenceNumber: 2, scheduledFor: '2026-09-26T13:00:00Z', sizeInBytes: 2, contentType: null, bodyPreview: '{}' },
    ] })
    render(<QueryClientProvider client={new QueryClient()}><ScheduledView namespaceId="n" entity="orders" /></QueryClientProvider>)
    const table = await screen.findByRole('table', { name: /soonest first/ })
    expect(within(table).getAllByRole('row').slice(1).map((r) => within(r).getAllByRole('cell')[1].textContent)).toEqual(['soon', 'later'])
    expect(screen.getByText(/there may be more/)).toBeInTheDocument()
  })
})
