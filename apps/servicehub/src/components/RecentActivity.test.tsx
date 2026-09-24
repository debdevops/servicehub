import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, act } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import * as identity from '../lib/api/identity'
import * as stream from '../lib/eventStream'
import { RecentActivity } from './RecentActivity'

vi.mock('../lib/api/identity')
const auditMock = vi.mocked(identity.fetchAudit)

const entry = (id: string, over: object = {}) => ({
  id, timestamp: '2026-09-25T10:00:00Z', actor: { identity: 'session', kind: 'user', label: 'from this browser session', isSession: true },
  action: 'Replay.Message', outcome: 'Success', namespaceId: null, namespaceName: null, cloudProvider: null, environment: null, resourceName: 'm-7', errorDetails: null, correlationId: null, ...over,
}) as identity.AuditEntry

function renderIt() {
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><MemoryRouter><RecentActivity /></MemoryRouter></QueryClientProvider>)
}

describe('Recent activity', () => {
  beforeEach(() => auditMock.mockResolvedValue({ items: [entry('1')], page: 1, pageSize: 8, total: 1 }))
  afterEach(() => stream.stopEventStream())

  it('reads the durable history and says it is not live when the stream is not open', async () => {
    renderIt()
    expect(await screen.findByText('Replayed a message')).toBeInTheDocument()
    expect(screen.getByText('History — not live')).toBeInTheDocument()
    expect(screen.queryByText('Live')).toBeNull()
  })

  it('says Live only while the stream is open, and the list stays right when it is not', async () => {
    class Src { static last: Src; onopen: (() => void) | null = null; onerror: (() => void) | null = null; onmessage = null; readyState = 1; constructor() { Src.last = this } close() { this.readyState = 2 } static CLOSED = 2 }
    vi.stubGlobal('EventSource', Src)
    renderIt()
    await screen.findByText('Replayed a message')
    act(() => stream.startEventStream())
    act(() => Src.last.onopen?.())
    expect(await screen.findByText('Live')).toBeInTheDocument()
    Src.last.readyState = 2
    act(() => Src.last.onerror?.())
    expect(await screen.findByText('History — not live')).toBeInTheDocument()
    expect(screen.getByText('Replayed a message')).toBeInTheDocument()
    vi.unstubAllGlobals()
  })

  it('shows a failed action as one that did not go through', async () => {
    auditMock.mockResolvedValue({ items: [entry('2', { outcome: 'Failure', action: 'Namespace.Connect' })], page: 1, pageSize: 8, total: 1 })
    renderIt()
    expect(await screen.findByText(/did not go through/)).toBeInTheDocument()
  })
})
