import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useSearchParams } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as bulk from '@/lib/api/bulk'
import { bulkSelection } from '@/lib/bulkSelection'
import BulkReplayModal from '@/components/message/BulkReplayModal'
import { expectNoAxeViolations } from '@tests/support/axe'

vi.mock('@/lib/api/bulk', async (orig) => ({ ...(await orig<typeof import('@/lib/api/bulk')>()), previewBulk: vi.fn(), startBulk: vi.fn(), fetchBulk: vi.fn(), cancelBulk: vi.fn() }))

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

  it('has no accessibility violations (6.6)', async () => {
    vi.mocked(bulk.previewBulk).mockResolvedValue(preview())
    renderModal()
    await new Promise((r) => setTimeout(r, 150))
    await expectNoAxeViolations(document.body)
  })

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

  it('pages the list of messages ten at a time', async () => {
    const ids = Array.from({ length: 23 }, (_, i) => i + 1)
    bulkSelection.set({ ids })
    vi.mocked(bulk.previewBulk).mockResolvedValue(preview({ selected: 23, willReplay: 23, heldBackCount: 0, heldBack: [] }))
    const dl = await import('@/lib/api/deadLetters')
    const fetchOne = vi.spyOn(dl, 'fetchDeadLetter').mockImplementation(async (id) => ({ item: { id, messageId: `m-${id}`, namespaceId: 'n', entityName: 'orders', topicName: null, deadLetterReason: 'Validation', detectedAtUtc: new Date().toISOString(), deliveryCount: 1, sizeInBytes: 10 } } as never))
    renderModal()
    await screen.findByText('m-1')
    expect(screen.getByText('m-10')).toBeInTheDocument()
    expect(screen.queryByText('m-11')).not.toBeInTheDocument()
    expect(screen.getByText(/1–10 of 23/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Next page' }))
    expect(await screen.findByText('m-11')).toBeInTheDocument()
    expect(screen.queryByText('m-1')).not.toBeInTheDocument()
    fetchOne.mockRestore()
  })

  it('names a held-back message by its own ID, not only ServiceHub’s number', async () => {
    vi.mocked(bulk.previewBulk).mockResolvedValue(preview())
    const dl = await import('@/lib/api/deadLetters')
    const one = vi.spyOn(dl, 'fetchDeadLetter').mockImplementation(async (id) => ({ item: { id, messageId: `cloud-msg-${id}`, namespaceId: 'n', entityName: 'orders', topicName: null, deadLetterReason: 'Validation', detectedAtUtc: new Date().toISOString(), deliveryCount: 1, sizeInBytes: 10 } } as never))
    renderModal()
    const held = await screen.findByRole('region', { name: 'Held back' })
    expect(await within(held).findByText('cloud-msg-9')).toBeInTheDocument()
    one.mockRestore()
  })

  it('a failed preview says nothing was sent and Try again works, without re-opening (6.1)', async () => {
    vi.mocked(bulk.previewBulk).mockRejectedValueOnce(new Error('down')).mockResolvedValue(preview())
    renderModal()
    expect(await screen.findByText(/so nothing was sent/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }))
    expect(await screen.findByText('Preview — nothing has run yet')).toBeInTheDocument()
    expect(bulk.startBulk).not.toHaveBeenCalled()
  })

  it('a refused start is shown beside the buttons that were clicked, in the pinned footer (6.1)', async () => {
    vi.mocked(bulk.previewBulk).mockResolvedValue(preview())
    vi.mocked(bulk.startBulk).mockRejectedValue(new Error('gone'))
    renderModal()
    await screen.findByText('Preview — nothing has run yet')
    await userEvent.click(screen.getAllByRole('button', { name: /^Replay \d+/ })[0])
    const alert = await screen.findByRole('alert')
    expect(alert.closest('footer')).not.toBeNull()
  })

  it('starts only when the person chooses to, and moves to the running view in the URL', async () => {
    vi.mocked(bulk.previewBulk).mockResolvedValue(preview())
    vi.mocked(bulk.startBulk).mockResolvedValue({ id: 'job1' } as bulk.BulkProgress)
    vi.mocked(bulk.fetchBulk).mockResolvedValue({ id: 'job1', status: 'running', selected: 7, willReplay: 6, sent: 2, failed: 0, unknown: 0, remaining: 4, heldBack: 1, sampleOnly: false } as bulk.BulkProgress)
    renderModal()

    // Replay sits at the top (always in view) and at the bottom.
    expect(await screen.findAllByRole('button', { name: /Replay 6 messages/ })).toHaveLength(2)
    await userEvent.click(screen.getAllByRole('button', { name: /Replay 6 messages/ })[0])

    expect(bulk.startBulk).toHaveBeenCalledWith('p1', false)
    await waitFor(() => expect(screen.getByTestId('url')).toHaveTextContent('job=job1'))
    expect(await screen.findByText('2 of 6 tried')).toBeInTheDocument()
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

    expect(await screen.findByRole('heading', { name: 'Bulk replay stopped itself' })).toBeInTheDocument()
    expect(screen.getAllByRole('status').some((e) => e.textContent === 'Bulk replay stopped itself')).toBe(true) // the verdict is announced, not just drawn
    expect(screen.getByText(/5 messages in a row were not accepted/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Stop now' })).not.toBeInTheDocument()
  })
})
