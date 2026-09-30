import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { choose } from '../../../support/choose'
import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as replayApi from '@/lib/api/replay'
import type { ReplayListItem, ReplayPage } from '@/lib/api/replay'
import { ReplayedTab } from '@/components/message/ReplayedTab'

vi.mock('@/lib/api/replay')
const listMock = vi.mocked(replayApi.fetchReplays)

const row = (over: Partial<ReplayListItem> = {}): ReplayListItem => ({
  id: 1, dlqMessageId: 7, namespaceId: 'n1', provider: 'azure', messageId: 'm-7', sourceEntity: 'payments-dlq', targetEntity: 'payments', replayedAt: '2026-09-25T10:00:00Z',
  replayedBy: 'session', actor: { identity: 'session', kind: 'user', label: 'from this browser session', isSession: true }, outcomeStatus: 'accepted', entryState: 'Observing', observationWindowEndsAt: null, markerApplied: true,
  verification: { status: 'watching', reasonCode: null, confidence: null, watchUntil: '2026-09-26T10:00:00Z', canConfirm: true, remedy: null }, ...over,
})
const page = (items: ReplayListItem[]): ReplayPage => ({ items, total: items.length, page: 1, pageSize: 25 })

function renderTab(url = '/?tab=replayed') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[url]}>
        <ReplayedTab provider="azure" choice={{ ns: null, env: null, namespaces: [] }} />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('the Replayed tab', () => {
  beforeEach(() => listMock.mockReset())

  it('says a replay is being watched — and never "stayed fixed" for one nothing has verified', async () => {
    listMock.mockResolvedValue(page([row()]))
    renderTab()
    expect(await screen.findByText(/Watching · until/)).toBeInTheDocument()
    // The row says nothing is verified; the explainer's definition of the term is not a claim about this row.
    expect(within(screen.getByRole('table')).queryByText(/stayed fixed/i)).toBeNull()
  })

  it('says which day a watch ends when it is not today — a bare clock time reads as the replay time', async () => {
    listMock.mockResolvedValue(page([row({ verification: { status: 'watching', reasonCode: null, confidence: null, watchUntil: '2020-01-02T20:06:00Z', canConfirm: true, remedy: null } })]))
    renderTab()
    expect(await screen.findByText(/Watching · until Jan \d+, \d\d:\d\d/)).toBeInTheDocument()
  })

  it('shows the same replay differently on a cloud that can prove it and one that cannot', async () => {
    const v = (over: object) => ({ status: 'verified', reasonCode: null, confidence: null, watchUntil: null, canConfirm: true, remedy: null, ...over }) as ReplayListItem['verification']
    listMock.mockResolvedValue(page([
      row({ id: 1, verification: v({}) }),
      row({ id: 2, provider: 'aws', verification: v({ status: 'verification_required', canConfirm: false, remedy: 'SETUP_DLQ_OBSERVER', reasonCode: 'AWS_NO_ABSENCE_PROOF' }) }),
      row({ id: 3, verification: v({ status: 'returned', confidence: 'Exact' }) }),
    ]))
    renderTab()
    const table = within(await screen.findByRole('table'))
    expect(table.getByText('Verified — stayed fixed')).toBeInTheDocument()
    expect(table.getByText('Verification required')).toBeInTheDocument()
    expect(table.getByText('Came back')).toBeInTheDocument()
  })

  it('never dresses a browser session up as a person', async () => {
    listMock.mockResolvedValue(page([row()]))
    renderTab()
    expect(await screen.findByText('This browser session')).toBeInTheDocument()
  })

  it('shows a refused replay and an unknown one as what they are', async () => {
    const base = row().verification
    listMock.mockResolvedValue(page([
      row({ id: 2, outcomeStatus: 'rejected', verification: { ...base, status: 'not_sent' } }),
      row({ id: 3, outcomeStatus: 'unknown', verification: { ...base, status: 'unknown' } }),
    ]))
    renderTab()
    const table = within(await screen.findByRole('table'))
    expect(table.getByText('Not accepted')).toBeInTheDocument()
    expect(table.getByText('Outcome unknown')).toBeInTheDocument()
  })

  it('links each row back to its message in the drawer', async () => {
    listMock.mockResolvedValue(page([row()]))
    renderTab()
    const link = await screen.findByRole('link', { name: /Details of message m-7/ })
    expect(link).toHaveAttribute('href', expect.stringContaining('message=7'))
  })

  it('says so when nothing has been replayed', async () => {
    listMock.mockResolvedValue(page([]))
    renderTab()
    expect(await screen.findByText(/Nothing has been replayed in Azure yet/)).toBeInTheDocument()
  })

  it('asks for the last 24 hours by default and passes the chosen result, who and word to the API', async () => {
    listMock.mockResolvedValue(page([row()]))
    renderTab()
    await screen.findByRole('table')
    expect(listMock).toHaveBeenLastCalledWith(expect.objectContaining({ provider: 'azure', window: '24h', ending: undefined, by: undefined }))
    await choose(screen.getByLabelText('Result'), 'returned')
    await choose(screen.getByLabelText('Replayed by'), 'autonomous')
    await choose(screen.getByLabelText('Time window'), '7d')
    await screen.findByRole('table')
    expect(listMock).toHaveBeenLastCalledWith(expect.objectContaining({ ending: 'returned', by: 'autonomous', window: '7d' }))
  })

  it('shows each replay’s own message ID in full, and Details puts away any other window left in the address', async () => {
    listMock.mockResolvedValue(page([row({ messageId: 'cloud-msg-0001-abcd' })]))
    renderTab('/?tab=replayed&modal=send&active=4')
    expect(await screen.findByText('cloud-msg-0001-abcd')).toBeInTheDocument()
    const href = screen.getByRole('link', { name: 'Details of message cloud-msg-0001-abcd' }).getAttribute('href')!
    expect(href).toContain('message=7')
    expect(href).not.toContain('modal=')
    expect(href).not.toContain('active=')
  })

  it('says filters matched nothing, and offers to clear them', async () => {
    listMock.mockResolvedValue(page([]))
    renderTab('/?tab=replayed&ending=fixed')
    expect(await screen.findByText('No replays match these filters.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Clear the filters' })).toBeInTheDocument()
  })
})
