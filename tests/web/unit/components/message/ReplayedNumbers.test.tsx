import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import * as recovery from '@/lib/api/recovery'
import { ReplayedNumbers } from '@/components/message/ReplayedNumbers'

vi.mock('@/lib/api/recovery')

const states = (over: Record<string, number>) => Object.entries(over).map(([state, count]) => ({ state, count }))
const summary = (over: Record<string, number>, replaysAccepted: number) =>
  ({ window: '24h', total: Object.values(over).reduce((a, b) => a + b, 0), states: states(over), byProvider: [], stayedFixedRate: 0.8, returnedConfidence: { exact: 1, heuristic: 0 }, replaysAccepted }) as never

function renderTiles() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter><ReplayedNumbers provider="azure" choice={{ ns: null, env: null, namespaces: [] }} /></MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('the replayed tile', () => {
  it('says why it is smaller than the list when some attempts were not accepted — and never counts a declined one as tried', async () => {
    vi.mocked(recovery.fetchRecoverySummary).mockResolvedValue(summary({ Recovered: 420, Returned: 7, Observing: 12, ExecutionFailed: 2, ExecutionUnknown: 5, Declined: 28 }, 439))
    renderTiles()
    expect(await screen.findByText('439')).toBeInTheDocument()
    expect(screen.getByText('last 24 hours · 7 more tried, not accepted')).toBeInTheDocument()
  })

  it('adds nothing when every attempt was accepted', async () => {
    vi.mocked(recovery.fetchRecoverySummary).mockResolvedValue(summary({ Recovered: 4, Returned: 1, Declined: 3 }, 5))
    renderTiles()
    expect(await screen.findByText('last 24 hours')).toBeInTheDocument()
    expect(screen.queryByText(/not accepted/)).toBeNull()
  })
})
