import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as dl from '../../lib/api/deadLetters'
import * as replayApi from '../../lib/api/replay'
import type { DeadLetterDetail } from '../../lib/api/deadLetters'
import { explainFailure } from '../../lib/analyzer'
import { MessageDrawer } from './MessageDrawer'
import { expectNoAxeViolations } from '../../test/axe'

vi.mock('../../lib/api/deadLetters')
vi.mock('../../lib/api/replay')
const fetchOne = vi.mocked(dl.fetchDeadLetter)

const detail = (over: Partial<DeadLetterDetail> = {}): DeadLetterDetail => ({
  item: {
    id: 7, namespaceId: 'n1', messageId: 'm-7', sequenceNumber: 7, entityName: 'orders', entityType: 'queue', topicName: null,
    detectedAtUtc: '2026-09-24T10:12:00Z', enqueuedTimeUtc: '2026-09-24T09:00:00Z', deliveryCount: 3, sizeInBytes: 2048,
    deadLetterReason: 'ValidationFailed', deadLetterErrorDescription: 'missing required field: customerId', status: 'active',
  },
  bodyPreview: '{"orderId":1,"total":5}', bodyIsPreview: false, contentType: 'application/json', correlationId: 'c-1', sessionId: null,
  applicationPropertiesJson: '{"source":"web"}', resolvedAt: null, othersLikeIt: 0, ...over,
})

function Where() {
  const { search } = useLocation()
  return <output data-testid="where">{search}</output>
}

function renderDrawer(url = '/?tab=dlq&message=7') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[url]}>
        <Where />
        <MessageDrawer />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('the message drawer', () => {
  beforeEach(() => {
    fetchOne.mockReset()
    vi.mocked(replayApi.fetchReplays).mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 1 })
  })

  it('has no accessibility violations (6.6)', async () => {
    renderDrawer()
    await new Promise((r) => setTimeout(r, 150))
    await expectNoAxeViolations(document.body)
  })

  it('draws nothing without ?message=', () => {
    renderDrawer('/?tab=dlq')
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('puts what failed first, then a badged suggestion, then the body with the failing field marked', async () => {
    fetchOne.mockResolvedValue(detail())
    renderDrawer()
    expect(await screen.findByText('ValidationFailed')).toBeInTheDocument()
    expect(screen.getByText('Suggestion')).toBeInTheDocument()
    expect(screen.getByText(/✕ missing required field: customerId/)).toBeInTheDocument()
    const order = ['What failed', 'Why it failed', 'Message body', 'Details'].map((n) => screen.getByLabelText(n).compareDocumentPosition.bind(screen.getByLabelText(n)))
    expect(order).toHaveLength(4)
    const [a, b, c, d] = ['What failed', 'Why it failed', 'Message body', 'Details'].map((n) => screen.getByLabelText(n))
    for (const [x, y] of [[a, b], [b, c], [c, d]]) expect(x.compareDocumentPosition(y) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('says so when only a preview of the body is kept', async () => {
    fetchOne.mockResolvedValue(detail({ bodyIsPreview: true }))
    renderDrawer()
    expect(await screen.findByText(/Only the first 23 characters/)).toBeInTheDocument()
  })

  it('says plainly when no body was kept', async () => {
    fetchOne.mockResolvedValue(detail({ bodyPreview: null }))
    renderDrawer()
    expect(await screen.findByText(/did not keep a body/)).toBeInTheDocument()
  })

  it('opens the replay proposal from the hero button — it never replays by itself', async () => {
    fetchOne.mockResolvedValue(detail())
    const user = userEvent.setup()
    renderDrawer()
    expect(await screen.findByText(/see exactly what will happen before it runs/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /Replay this message/ }))
    expect(screen.getByTestId('where').textContent).toContain('modal=replay')
    expect(screen.getByTestId('where').textContent).toContain('message=7')
  })

  it('keeps Replay but disables it, with the reason, when the message has left the queue', async () => {
    fetchOne.mockResolvedValue(detail({ item: { ...detail().item, status: 'resolved' } }))
    renderDrawer()
    expect(await screen.findByRole('button', { name: /Replay this message/ })).toBeDisabled()
    expect(screen.getByText(/nothing to replay/)).toBeInTheDocument()
  })

  it('leaves Esc to the proposal while it is open on top', async () => {
    fetchOne.mockResolvedValue(detail())
    const user = userEvent.setup()
    renderDrawer('/?tab=dlq&message=7&modal=replay')
    await screen.findByText('ValidationFailed')
    await user.keyboard('{Escape}')
    expect(screen.getByTestId('where').textContent).toContain('message=7')
  })

  it('expands to the full view and Esc returns to the side view, then closes', async () => {
    fetchOne.mockResolvedValue(detail())
    const user = userEvent.setup()
    renderDrawer()
    await user.click(await screen.findByRole('button', { name: 'Expand' }))
    expect(screen.getByTestId('where').textContent).toContain('view=full')
    expect(screen.getByRole('dialog').getAttribute('data-overlay')).toBe('modal')
    await user.keyboard('{Escape}')
    expect(screen.getByTestId('where').textContent).not.toContain('view=full')
    expect(screen.getByTestId('where').textContent).toContain('message=7')
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.getByTestId('where').textContent).not.toContain('message='))
    expect(screen.getByTestId('where').textContent).toContain('tab=dlq')
  })

  it('switches tabs in the side view', async () => {
    fetchOne.mockResolvedValue(detail())
    const user = userEvent.setup()
    renderDrawer()
    await user.click(await screen.findByRole('tab', { name: 'Properties' }))
    expect(screen.getByText('source')).toBeInTheDocument()
    expect(screen.queryByLabelText('What failed')).toBeNull()
  })

  it('docks beside the page: no scrim, and the page behind stays reachable', async () => {
    fetchOne.mockResolvedValue(detail())
    renderDrawer()
    await screen.findByText('ValidationFailed')
    expect(screen.queryByTestId('overlay-scrim')).toBeNull()
    expect(screen.getByRole('dialog').getAttribute('data-docked')).toBe('true')
  })

  it('marks the missing field inside the body block, where it would have been', async () => {
    fetchOne.mockResolvedValue(detail())
    renderDrawer()
    expect(await screen.findByText(/\+ "customerId": missing/)).toBeInTheDocument()
    expect(screen.getByText(/✕ missing required field: customerId/)).toBeInTheDocument()
  })

  it('offers Copy and Formatted/Raw for a body that parses, and neither toggle for a cut one', async () => {
    fetchOne.mockResolvedValue(detail())
    const user = userEvent.setup()
    const { unmount } = renderDrawer()
    await screen.findByText('ValidationFailed')
    expect(screen.getByRole('button', { name: 'Copy' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Raw' }))
    expect(screen.getByRole('button', { name: 'Raw' })).toHaveAttribute('aria-pressed', 'true')
    unmount()

    fetchOne.mockResolvedValue(detail({ bodyIsPreview: true }))
    renderDrawer()
    await screen.findByText('ValidationFailed')
    expect(screen.queryByRole('button', { name: 'Formatted' })).toBeNull()
  })

  it('says how many others failed the same way', async () => {
    fetchOne.mockResolvedValue(detail({ othersLikeIt: 6 }))
    renderDrawer()
    expect(await screen.findByText(/6 other messages/)).toBeInTheDocument()
  })

  it('the full view is two columns with the reading actions in its footer, and Others like it links to the list', async () => {
    fetchOne.mockResolvedValue(detail({ othersLikeIt: 2 }))
    renderDrawer('/?tab=dlq&message=7&view=full')
    expect((await screen.findAllByRole('button', { name: /Copy message ID/ })).length).toBeGreaterThanOrEqual(1)
    expect(screen.getByRole('button', { name: /Copy link/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Download body/ })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Show them/ })).toHaveAttribute('href', expect.stringContaining('reason=ValidationFailed'))
    expect(screen.getByRole('dialog').getAttribute('data-overlay')).toBe('modal')
  })

  it('says a missing message is missing, not broken', async () => {
    fetchOne.mockRejectedValueOnce(Object.assign(new Error('not found'), { response: { status: 404 } }))
    renderDrawer()
    await waitFor(() => expect(document.body.textContent).toMatch(/no dead letter/))
  })
})

describe('explainFailure', () => {
  it('finds the failing field the error names', () => {
    expect(explainFailure('ValidationFailed', 'missing required field: customerId', 3).failingField).toBe('customerId')
  })
  it('does not invent a field or a reading', () => {
    const e = explainFailure('Weird', 'no idea', 1)
    expect(e.failingField).toBeNull()
    expect(e.summary).toMatch(/no reading/)
  })
})
