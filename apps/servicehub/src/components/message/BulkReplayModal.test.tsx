import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useSearchParams } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as bulk from '../../lib/api/bulk'
import { bulkSelection } from '../../lib/bulkSelection'
import BulkReplayModal from './BulkReplayModal'

vi.mock('../../lib/api/bulk', async (orig) => ({ ...(await orig<typeof import('../../lib/api/bulk')>()), previewBulk: vi.fn(), startBulk: vi.fn(), fetchBulk: vi.fn(), cancelBulk: vi.fn() }))

const preview = (over: Partial<bulk.BulkPreview> = {}): bulk.BulkPreview => ({
  previewId: 'p1', selected: 7, willReplay: 6, heldBackCount: 1,
  groups: [{ reason: 'Validation', selected: 7, willReplay: 6, heldBack: 1 }],
  heldBack: [{ dlqMessageId: 9, entityName: 'orders', deadLetterReason: 'Validation', reasonCode: 'RECURRENCE_CAP_EXCEEDED', remedy: 'It has already been replayed and came back too many times.' }],
  perSecond: 2, stopAfterConsecutiveFailures: 5, expiresInMinutes: 30, canProveDlqAbsence: true, ...over,
})

function Url() { return <output data-testid="url">{useSearchParams()[0].toString()}</output> }

function renderModal(initial = '/?modal=bulk-replay') {
  return render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter initialEntries={[initial]}><BulkReplayModal entry={{} as never} close={() => {}} /><Url /></MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('Bulk Replay modal', () => {
  beforeEach(() => { vi.clearAllMocks(); bulkSelection.set({ ids: [1, 2, 3, 4, 5, 6, 7] }) })

  it('shows the preview first, sends nothing, and lists each held-back message with its reason and remedy', async () => {
    vi.mocked(bulk.previewBulk).mockResolvedValue(preview())
    renderModal()

    expect(await screen.findByText('Preview — nothing has run yet')).toBeInTheDocument()
    expect(bulk.previewBulk).toHaveBeenCalledWith([1, 2, 3, 4, 5, 6, 7])
    const held = screen.getByRole('region', { name: 'Held back' })
    expect(within(held).getByText(/came back too many times/)).toBeInTheDocument()
    expect(within(held).getByText('RECURRENCE_CAP_EXCEEDED')).toBeInTheDocument()
    expect(bulk.startBulk).not.toHaveBeenCalled()
  })

  it('starts only when the person chooses to, and moves to the running view in the URL', async () => {
    vi.mocked(bulk.previewBulk).mockResolvedValue(preview())
    vi.mocked(bulk.startBulk).mockResolvedValue({ id: 'job1' } as bulk.BulkProgress)
    vi.mocked(bulk.fetchBulk).mockResolvedValue({ id: 'job1', status: 'running', selected: 7, willReplay: 6, sent: 2, failed: 0, unknown: 0, remaining: 4, heldBack: 1, sampleOnly: false } as bulk.BulkProgress)
    renderModal()

    await userEvent.click(await screen.findByRole('button', { name: /Replay 6 messages/ }))

    expect(bulk.startBulk).toHaveBeenCalledWith('p1', false)
    await waitFor(() => expect(screen.getByTestId('url')).toHaveTextContent('job=job1'))
    expect(await screen.findByText('2 of 6 sent')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Stop now' })).toBeInTheDocument()
  })

  it('offers a sample of 1 when everything failed on validation', async () => {
    vi.mocked(bulk.previewBulk).mockResolvedValue(preview())
    vi.mocked(bulk.startBulk).mockResolvedValue({ id: 'job2' } as bulk.BulkProgress)
    renderModal()

    await userEvent.click(await screen.findByRole('button', { name: /Replay a sample of 1/ }))

    expect(bulk.startBulk).toHaveBeenCalledWith('p1', true)
  })

  it('tells a page opened with no selection to choose messages first, and previews nothing', async () => {
    bulkSelection.set(null)
    renderModal()

    expect(await screen.findByText(/Choose some dead letters in the table first/)).toBeInTheDocument()
    expect(bulk.previewBulk).not.toHaveBeenCalled()
  })

  it('says plainly when a run stopped itself, and never offers Stop after it ended', async () => {
    vi.mocked(bulk.fetchBulk).mockResolvedValue({
      id: 'j', status: 'stopped', selected: 9, willReplay: 9, sent: 0, failed: 5, unknown: 0, remaining: 0, heldBack: 0, sampleOnly: false,
      endedReason: 'Stopped itself: 5 messages in a row were not accepted. Nothing further was sent.',
    } as bulk.BulkProgress)
    renderModal('/?modal=bulk-replay&job=j')

    expect(await screen.findByText('Bulk replay stopped itself')).toBeInTheDocument()
    expect(screen.getByText(/5 messages in a row were not accepted/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Stop now' })).not.toBeInTheDocument()
  })
})
