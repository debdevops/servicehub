import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as identity from '../../lib/api/identity'
import * as messages from '../../lib/api/messages'
import * as ns from '../../lib/api/namespaces'
import { navigation, type OverlayEntry } from '../../nav/navigation'
import { ProviderScopeContext } from '../provider/providerScope'
import SendMessageModal from './SendMessageModal'
import { expectNoAxeViolations } from '../../test/axe'

vi.mock('../../lib/api/namespaces')
vi.mock('../../lib/api/messages')
vi.mock('../../lib/api/identity', async (original) => ({ ...(await original<typeof identity>()), fetchMe: vi.fn() }))

const entry = navigation.find((e) => e.id === 'send') as OverlayEntry
const namespace = (id: string, environment: ns.EnvironmentKind) => ({ id, name: `orders-${environment}`, displayName: null, provider: 'azure', environment }) as unknown as ns.Namespace

function wrap() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <ProviderScopeContext.Provider value={{ selected: 'azure', select: () => {} }}>
          <SendMessageModal entry={entry} close={() => {}} />
        </ProviderScopeContext.Provider>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('Send a message', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(identity.fetchMe).mockResolvedValue({ ownerId: 'o', authMethod: 'session', actor: { identity: 's', kind: 'user', label: 'l', isSession: true }, effectiveRole: 'Admin', governanceActive: false } as identity.Me)
    vi.mocked(ns.fetchNamespaces).mockResolvedValue([namespace('d', 'dev'), namespace('p', 'prod')])
    vi.mocked(ns.fetchEntities).mockResolvedValue({ namespaceId: 'd', entities: [{ name: 'orders', kind: 'queue', activeMessages: 0, deadLetterMessages: 0, deadLetterTargetName: null }, { name: 'events', kind: 'topic', activeMessages: null, deadLetterMessages: null, deadLetterTargetName: null }, { name: 'events/sub', kind: 'subscription', activeMessages: 0, deadLetterMessages: 0, deadLetterTargetName: null }] })
    vi.mocked(messages.sendMessage).mockResolvedValue({ accepted: true, detail: 'The cloud accepted one message onto orders.' })
  })

  it('has no accessibility violations (6.6)', async () => {
    const { container } = wrap()
    await screen.findByRole('option', { name: 'orders' })
    await expectNoAxeViolations(container)
  })

  it('never offers a production namespace, or a subscription as a target', async () => {
    wrap()
    expect(await screen.findByText(/Production namespaces are not listed/)).toBeInTheDocument()
    expect(await screen.findByRole('option', { name: 'orders' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'events (topic)' })).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: /events\/sub/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('option', { name: /prod/i })).not.toBeInTheDocument()
  })

  it('checks JSON before sending, then sends one message with its properties and says what the cloud accepted', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByRole('option', { name: 'orders' })
    const body = screen.getByLabelText('Body')
    await user.clear(body)
    await user.type(body, 'not json')
    expect(screen.getByRole('alert')).toHaveTextContent(/not valid JSON/)
    expect(screen.getByRole('button', { name: 'Send' })).toBeDisabled()

    await user.clear(body)
    await user.type(body, '{{"a":1}')
    await user.click(screen.getByRole('button', { name: /Add a property/ }))
    await user.type(screen.getByLabelText('Property 1 name'), 'source')
    await user.type(screen.getByLabelText('Property 1 value'), 'test')
    await user.click(screen.getByRole('button', { name: 'Send' }))

    expect(vi.mocked(messages.sendMessage).mock.calls[0][0]).toMatchObject({ namespaceId: 'd', entity: 'orders', isTopic: false, body: '{"a":1}', properties: { source: 'test' } })
    expect(await screen.findByText(/accepted one message onto orders/)).toBeInTheDocument()
  })
})
