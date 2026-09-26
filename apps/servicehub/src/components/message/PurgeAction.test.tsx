import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as identity from '../../lib/api/identity'
import * as ns from '../../lib/api/namespaces'
import * as replay from '../../lib/api/replay'
import { PurgeAction } from './PurgeAction'

vi.mock('../../lib/api/namespaces')
vi.mock('../../lib/api/replay')
vi.mock('../../lib/api/identity', async (original) => ({ ...(await original<typeof identity>()), fetchMe: vi.fn() }))

const namespace = (id: string, supportsPurge: boolean) => ({ id, name: id, provider: supportsPurge ? 'aws' : 'azure', environment: 'dev', capabilities: { supportsPurge } }) as unknown as ns.Namespace

function wrap(namespaceId: string, active = true) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}><PurgeAction dlqMessageId={7} namespaceId={namespaceId} active={active} /></QueryClientProvider>)
}

describe('Purge from the drawer', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(identity.fetchMe).mockResolvedValue({ ownerId: 'o', authMethod: 'session', actor: { identity: 's', kind: 'user', label: 'l', isSession: true }, effectiveRole: 'Admin', governanceActive: false } as identity.Me)
    vi.mocked(ns.fetchNamespaces).mockResolvedValue([namespace('aws', true), namespace('azure', false)])
  })

  it('where the cloud cannot delete one message, says so and offers no button', async () => {
    wrap('azure')
    expect(await screen.findByText(/can’t delete one message/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Purge/ })).not.toBeInTheDocument()
  })

  it('needs a reason, then purges once and shows what the cloud did', async () => {
    const user = userEvent.setup()
    vi.mocked(replay.purgeMessage).mockResolvedValue({ entryId: 'e', operationId: 'o', result: 'accepted', state: 'Discarded', markerApplied: false, observationWindowEndsAt: null, message: 'Deleted from the dead-letter queue, for good.', errorCode: null } as never)
    wrap('aws')
    await user.click(await screen.findByRole('button', { name: /Purge instead/ }))
    const go = screen.getByRole('button', { name: 'Purge for good' })
    expect(go).toBeDisabled()
    await user.type(screen.getByLabelText('Why purge it'), 'poison')
    await user.click(go)
    expect(replay.purgeMessage).toHaveBeenCalledWith(7, 'poison')
    expect(await screen.findByText(/for good/)).toBeInTheDocument()
  })

  it('is absent once the message has left the queue', async () => {
    const { container } = wrap('aws', false)
    await screen.findByText((_, el) => el === container)
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })
})
