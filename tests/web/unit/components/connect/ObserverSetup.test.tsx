import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as namespacesApi from '@/lib/api/namespaces'
import type { DlqObserver, Namespace } from '@/lib/api/namespaces'
import { ObserverSetup } from '@/components/connect/ObserverSetup'

vi.mock('@/lib/api/namespaces')
const api = vi.mocked(namespacesApi)

const ns = (provider: Namespace['provider'], canProve: boolean): Namespace =>
  ({ id: 'n1', name: 'orders', displayName: 'Orders', provider, capabilities: { canProveDlqAbsence: canProve } as Namespace['capabilities'] }) as Namespace

const observer = (over: Partial<DlqObserver> = {}): DlqObserver => ({
  needed: true, enabled: false, live: false, observerReference: null, dlqEntityName: null, stalenessBoundMinutes: 30,
  lastCanarySentAt: null, lastConfirmedAt: null, status: 'No observer is set up, so a replay here can be sent back but not confirmed as fixed.', ...over,
})

const renderIt = (n: Namespace) =>
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })}>
      <ObserverSetup ns={n} />
    </QueryClientProvider>,
  )

describe('the observer setup on a connection', () => {
  beforeEach(() => vi.resetAllMocks())

  it('is not shown for a cloud that can confirm on its own, and never asks the API', () => {
    renderIt(ns('azure', true))
    expect(screen.queryByText(/dead-letter observer/i)).toBeNull()
    expect(api.fetchDlqObserver).not.toHaveBeenCalled()
  })

  it('says turned-on is not the same as working: it shows the API’s own status, never its own claim', async () => {
    api.fetchDlqObserver.mockResolvedValue(observer({ enabled: true, observerReference: 't', dlqEntityName: 'q', status: 'Turned on, but the observer’s log has not shown a test message yet — not confirming anything.' }))
    renderIt(ns('aws', false))
    expect(await screen.findByText(/not confirming anything/)).toBeInTheDocument()
    expect(screen.queryByText(/is working/i)).toBeNull()
  })

  it('turns the observer on with the names given, and uses the cloud’s own words for them', async () => {
    api.fetchDlqObserver.mockResolvedValue(observer())
    api.configureDlqObserver.mockResolvedValue(observer({ enabled: true, observerReference: 'obs-table', dlqEntityName: 'orders-dlq' }))
    renderIt(ns('aws', false))

    await userEvent.click(await screen.findByRole('button', { name: 'Set up the observer' }))
    const turnOn = screen.getByRole('button', { name: 'Turn on' })
    expect(turnOn).toBeDisabled()
    await userEvent.type(screen.getByLabelText(/DynamoDB/), 'obs-table')
    await userEvent.type(screen.getByLabelText(/SQS queue name/), 'orders-dlq')
    await userEvent.click(turnOn)

    await waitFor(() => expect(api.configureDlqObserver).toHaveBeenCalledWith('n1', { enabled: true, observerReference: 'obs-table', dlqEntityName: 'orders-dlq' }))
  })

  it('shows why the API refused rather than closing as if it worked', async () => {
    api.fetchDlqObserver.mockResolvedValue(observer())
    api.configureDlqObserver.mockRejectedValue(new Error('nope'))
    renderIt(ns('gcp', false))

    await userEvent.click(await screen.findByRole('button', { name: 'Set up the observer' }))
    await userEvent.type(screen.getByLabelText(/Firestore/), 'c')
    await userEvent.type(screen.getByLabelText(/Pub\/Sub topic/), 't')
    await userEvent.click(screen.getByRole('button', { name: 'Turn on' }))

    expect(await screen.findByRole('alert')).toBeInTheDocument()
  })
})
