import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as api from '../../lib/api/namespaces'
import type { Namespace } from '../../lib/api/namespaces'
import { EntityPicker } from './EntityPicker'

vi.mock('../../lib/api/namespaces')

const ns = { id: 'n1', name: 'n1' } as Namespace
const ent = (name: string, kind: 'queue' | 'topic' | 'subscription', dl: number | null = 0) => ({ name, kind, activeMessages: 0, deadLetterMessages: dl, deadLetterTargetName: null })

function renderPicker(over: { value?: string; onChange?: (e: string | null) => void; recorded?: string[] } = {}) {
  const onChange = over.onChange ?? vi.fn()
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <EntityPicker namespaces={[ns]} cloud="Azure" recorded={over.recorded ?? []} value={over.value} onChange={onChange} />
    </QueryClientProvider>,
  )
  return onChange
}

describe('the queue-or-topic picker', () => {
  beforeEach(() => vi.clearAllMocks())

  it('keeps queues and topics apart under their own titles, with each topic’s subscriptions beneath it', async () => {
    vi.mocked(api.fetchEntities).mockResolvedValue({
      namespaceId: 'n1',
      entities: [ent('orders', 'queue', 72), ent('orders-topic', 'topic'), ent('orders-topic/subscriptions/billing', 'subscription', 14)],
    })
    renderPicker()

    await userEvent.click(screen.getByRole('button', { name: /All queues & topics/ }))
    const list = await screen.findByRole('listbox', { name: 'Queue or topic' })

    expect(within(list).getByText('Queues')).toBeInTheDocument()
    expect(within(list).getByText('Topics')).toBeInTheDocument()
    expect(within(list).getByRole('option', { name: /orders.*72/ })).toBeInTheDocument()
    expect(within(list).getByText('orders-topic')).toBeInTheDocument()
    expect(within(list).getByRole('option', { name: /billing.*14/ })).toBeInTheDocument()
  })

  it('says “None in {cloud}” for a group the cloud does not have, instead of dropping it', async () => {
    vi.mocked(api.fetchEntities).mockResolvedValue({ namespaceId: 'n1', entities: [ent('orders', 'queue')] })
    renderPicker()

    await userEvent.click(screen.getByRole('button', { name: /All queues & topics/ }))

    expect(await screen.findByText('None in Azure')).toBeInTheDocument() // no topics
    expect(screen.getByText('Queues')).toBeInTheDocument()
  })

  it('filters by the name the dead letters are stored under — topic/subscriptions/name — on every cloud', async () => {
    vi.mocked(api.fetchEntities).mockResolvedValue({ namespaceId: 'n1', entities: [ent('orders-topic/billing', 'subscription', null)] }) // Google/AWS spell it topic/sub
    const onChange = renderPicker()

    await userEvent.click(screen.getByRole('button', { name: /All queues & topics/ }))
    await userEvent.click(await screen.findByRole('option', { name: /billing/ }))

    expect(onChange).toHaveBeenCalledWith('orders-topic/subscriptions/billing')
  })

  it('draws no number where the cloud cannot count', async () => {
    vi.mocked(api.fetchEntities).mockResolvedValue({ namespaceId: 'n1', entities: [ent('orders-topic/billing', 'subscription', null)] })
    renderPicker()

    await userEvent.click(screen.getByRole('button', { name: /All queues & topics/ }))

    expect((await screen.findByRole('option', { name: /billing/ })).textContent).not.toMatch(/\d/)
  })

  it('finds an entity by typing, and offers All to clear the filter', async () => {
    vi.mocked(api.fetchEntities).mockResolvedValue({ namespaceId: 'n1', entities: [ent('orders', 'queue'), ent('payments', 'queue')] })
    const onChange = renderPicker({ value: 'orders' })

    await userEvent.click(screen.getByRole('button', { name: /orders/ }))
    await userEvent.type(await screen.findByLabelText('Find a queue, topic or subscription'), 'pay')
    expect(screen.queryByRole('option', { name: /^orders/ })).not.toBeInTheDocument()
    expect(screen.getByRole('option', { name: /payments/ })).toBeInTheDocument()

    await userEvent.clear(screen.getByLabelText('Find a queue, topic or subscription'))
    await userEvent.click(screen.getByRole('option', { name: /All queues & topics/ }))
    expect(onChange).toHaveBeenCalledWith(null)
  })

  it('keeps a recorded entity selectable even when the cloud no longer lists it', async () => {
    vi.mocked(api.fetchEntities).mockResolvedValue({ namespaceId: 'n1', entities: [] })
    renderPicker({ recorded: ['old-queue'] })

    await userEvent.click(screen.getByRole('button', { name: /All queues & topics/ }))

    expect(await screen.findByRole('option', { name: /old-queue/ })).toBeInTheDocument()
  })

  it('closes on Escape', async () => {
    vi.mocked(api.fetchEntities).mockResolvedValue({ namespaceId: 'n1', entities: [] })
    renderPicker()

    await userEvent.click(screen.getByRole('button', { name: /All queues & topics/ }))
    await userEvent.keyboard('{Escape}')

    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  })
})
