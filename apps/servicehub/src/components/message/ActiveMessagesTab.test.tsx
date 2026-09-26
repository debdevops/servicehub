import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as api from '../../lib/api/namespaces'
import * as messages from '../../lib/api/messages'
import type { CloudProvider, Namespace } from '../../lib/api/namespaces'
import { ActiveMessagesTab } from './ActiveMessagesTab'

vi.mock('../../lib/api/namespaces')
vi.mock('../../lib/api/messages')

const ns = (provider: CloudProvider, peek: boolean): Namespace =>
  ({ id: `${provider}1`, name: provider, provider, capabilities: { supportsRepeatablePeek: peek } }) as unknown as Namespace

const renderTab = (provider: CloudProvider, n: Namespace | readonly Namespace[]) =>
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter initialEntries={['/?tab=active']}>
        <ActiveMessagesTab provider={provider} namespaces={Array.isArray(n) ? n : [n]} />
      </MemoryRouter>
    </QueryClientProvider>,
  )

const queue = (active: number | null) => ({ name: 'orders', kind: 'queue' as const, activeMessages: active, deadLetterMessages: 0, deadLetterTargetName: null })

describe('Active messages tab', () => {
  beforeEach(() => vi.clearAllMocks())

  it('lists messages where looking is safe, chosen by capability', async () => {
    vi.mocked(api.fetchEntities).mockResolvedValue({ namespaceId: 'azure1', entities: [queue(2)] })
    vi.mocked(messages.peekMessages).mockResolvedValue({
      namespaceId: 'azure1', entity: 'orders', subscription: null, deadLetter: false,
      messages: [{ messageId: 'm1', sequenceNumber: 7, body: '{}', enqueuedTime: new Date().toISOString(), deliveryCount: 2, sizeInBytes: 2048 } as messages.Message],
      paging: { requested: 25, returned: 1, nextFromSequenceNumber: null }, peek: { repeatable: true, warning: null },
    })
    renderTab('azure', ns('azure', true))

    expect(await screen.findByRole('table', { name: 'Active messages in orders' })).toBeInTheDocument()
    expect(screen.getByText('Details →')).toBeInTheDocument()
    expect(messages.peekMessages).toHaveBeenCalledWith('azure1', expect.objectContaining({ entity: 'orders' }))
  })

  it('names each queue with its namespace once several namespaces are in scope, and not before', async () => {
    const named = (id: string, displayName: string) => ({ ...ns('azure', true), id, displayName }) as Namespace
    vi.mocked(api.fetchEntities).mockImplementation(async (id) => ({ namespaceId: id, entities: [queue(2)] }))
    vi.mocked(messages.peekMessages).mockResolvedValue({
      namespaceId: 'x', entity: 'orders', subscription: null, deadLetter: false, messages: [],
      paging: { requested: 25, returned: 0, nextFromSequenceNumber: null }, peek: { repeatable: true, warning: null },
    })
    const view = renderTab('azure', [named('a', 'Azure Prod'), named('b', 'Azure Dev')])

    expect(await screen.findByRole('option', { name: 'Azure Prod / orders · 2' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'Azure Dev / orders · 2' })).toBeInTheDocument()
    view.unmount()

    renderTab('azure', [named('a', 'Azure Prod')])
    expect(await screen.findByRole('option', { name: 'orders · 2' })).toBeInTheDocument()
  })

  it('shows counts only, and never looks, where a look is a delivery', async () => {
    vi.mocked(api.fetchEntities).mockResolvedValue({ namespaceId: 'aws1', entities: [queue(462)] })
    renderTab('aws', ns('aws', false))

    expect(await screen.findByText(/counts active messages but doesn’t open them/)).toBeInTheDocument()
    expect(screen.getByText('462')).toBeInTheDocument()
    expect(messages.peekMessages).not.toHaveBeenCalled()
  })

  it('says "can’t count here" for a count the cloud cannot supply, never 0', async () => {
    vi.mocked(api.fetchEntities).mockResolvedValue({ namespaceId: 'gcp1', entities: [{ ...queue(null), deadLetterMessages: null }] })
    renderTab('gcp', ns('gcp', false))

    expect((await screen.findAllByText('can’t count here')).length).toBe(2)
    expect(screen.queryByText('0')).not.toBeInTheDocument()
  })
})
