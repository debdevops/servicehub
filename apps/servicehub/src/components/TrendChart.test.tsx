import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as dl from '../lib/api/deadLetters'
import type { DeadLetterTrend } from '../lib/api/deadLetters'
import TrendChart from './TrendChart'

vi.mock('../lib/api/deadLetters')
const trendMock = vi.mocked(dl.fetchDeadLetterTrend)

const trend = (days: number, over: Partial<DeadLetterTrend['series'][number]> = {}): DeadLetterTrend => ({
  days,
  series: Array.from({ length: days }, (_, i) => ({
    date: `2026-09-${String(25 - (days - 1) + i).padStart(2, '0')}`,
    new: i === days - 1 ? 4 : 0,
    resolved: i === days - 1 ? 2 : 0,
    ...over,
  })),
})

function renderChart() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(<QueryClientProvider client={client}><TrendChart provider="azure" /></QueryClientProvider>)
}

describe('the 7-day trend', () => {
  beforeEach(() => trendMock.mockReset())

  it('asks for 7 days first and says it is per day, not hourly', async () => {
    trendMock.mockResolvedValue(trend(7))
    renderChart()
    expect(await screen.findByText(/new vs resolved, last 7 days/)).toBeInTheDocument()
    expect(trendMock).toHaveBeenCalledWith('azure', 7)
    expect(screen.getByText(/Per day \(UTC\)/)).toBeInTheDocument()
    expect(screen.queryByText(/hour/i)).toBeNull()
  })

  it('re-asks when the range control changes', async () => {
    trendMock.mockImplementation(async (_p, d) => trend(d))
    const user = userEvent.setup()
    renderChart()
    await screen.findByText(/last 7 days/)
    await user.click(screen.getByRole('radio', { name: '30 days' }))
    await waitFor(() => expect(trendMock).toHaveBeenLastCalledWith('azure', 30))
    expect(await screen.findByText(/last 30 days/)).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: '30 days' })).toHaveAttribute('aria-checked', 'true')
  })

  it('offers a table of the same numbers', async () => {
    trendMock.mockResolvedValue(trend(7))
    const user = userEvent.setup()
    renderChart()
    await user.click(await screen.findByRole('button', { name: 'Show as table' }))
    const rows = screen.getAllByRole('row')
    expect(rows).toHaveLength(8) // header + 7 days
    expect(rows.at(-1)).toHaveTextContent('4')
    expect(rows.at(-1)).toHaveTextContent('2')
  })

  it('says plainly when there is nothing to draw', async () => {
    trendMock.mockResolvedValue(trend(7, { new: 0, resolved: 0 }))
    renderChart()
    expect(await screen.findByText(/Nothing new and nothing resolved in the last 7 days/)).toBeInTheDocument()
  })

  it('says so when the trend cannot be read', async () => {
    trendMock.mockRejectedValueOnce(new Error('boom'))
    renderChart()
    expect(await screen.findByRole('alert')).toHaveTextContent(/couldn’t read the trend/)
  })
})
