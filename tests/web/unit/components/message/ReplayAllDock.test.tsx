import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { act } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import * as bulk from '@/lib/api/bulk'
import * as deadLetters from '@/lib/api/deadLetters'
import { closeReplayAll, openReplayAll } from '@/lib/replayAll'
import { ReplayAllDock } from '@/components/message/ReplayAllDock'

vi.mock('@/lib/api/bulk', async (orig) => ({ ...(await orig<typeof import('@/lib/api/bulk')>()), previewBulk: vi.fn(), startBulk: vi.fn(), fetchBulk: vi.fn(), cancelBulk: vi.fn() }))
vi.mock('@/lib/api/deadLetters', async (orig) => ({ ...(await orig<typeof import('@/lib/api/deadLetters')>()), fetchDeadLetters: vi.fn() }))

const preview = (n: number): bulk.BulkPreview => ({
  previewId: 'p1', selected: n, willReplay: n, heldBackCount: 0, groups: [{ reason: 'PaymentTimeout', selected: n, willReplay: n, heldBack: 0 }],
  heldBack: [], perSecond: 2, stopAfterConsecutiveFailures: 5, expiresInMinutes: 30, canProveDlqAbsence: false,
})

async function open(cloudCount: number | null) {
  vi.mocked(deadLetters.fetchDeadLetters).mockResolvedValue({ items: Array.from({ length: 100 }, (_, i) => ({ id: i + 1 })), paging: { total: 100 } } as never)
  vi.mocked(bulk.previewBulk).mockResolvedValue(preview(100))
  render(
    <QueryClientProvider client={new QueryClient()}><MemoryRouter><ReplayAllDock /></MemoryRouter></QueryClientProvider>,
  )
  await act(async () => { await openReplayAll({ provider: 'aws' }, 'All namespaces in AWS', cloudCount) })
}

describe('Replay all preview wording', () => {
  afterEach(() => { closeReplayAll(); vi.clearAllMocks() })

  it('says the whole scope is replayed and that the list filters do not apply', async () => {
    await open(null)
    expect(await screen.findByText(/still stuck in/i)).toHaveTextContent('All namespaces in AWS')
    expect(screen.getByText(/filters, time window and search on the list do not apply/i)).toBeInTheDocument()
    expect(screen.queryByText(/this cloud counts/i)).not.toBeInTheDocument()
  })

  it('says how many of the cloud’s own count ServiceHub has recorded when the cloud counts more', async () => {
    await open(313)
    expect(await screen.findByText(/this cloud counts/i)).toHaveTextContent('100 of the 313')
  })

  it('stays quiet when everything the cloud counts is recorded', async () => {
    await open(100)
    await screen.findByText(/still stuck in/i)
    expect(screen.queryByText(/this cloud counts/i)).not.toBeInTheDocument()
  })
})
