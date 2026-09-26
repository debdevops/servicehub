import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as api from '../../lib/api/pendingWork'
import * as replay from '../../lib/api/replay'
import * as identity from '../../lib/api/identity'
import type { PendingWorkItem } from '../../lib/api/pendingWork'
import ApproveModal from './ApproveModal'

vi.mock('../../lib/api/pendingWork', async (original) => ({ ...(await original<typeof api>()), fetchPendingWork: vi.fn(), approvePending: vi.fn(), declinePending: vi.fn() }))
vi.mock('../../lib/api/replay', async (original) => ({ ...(await original<typeof replay>()), fetchReplayProposal: vi.fn() }))
vi.mock('../../lib/api/identity', async (original) => ({ ...(await original<typeof identity>()), fetchMe: vi.fn() }))

const item = (id: string, entity: string): PendingWorkItem => ({
  kind: 'approval', id, entryId: id, agentId: null, dlqMessageId: 7, namespaceId: 'n1', namespaceName: 'aws-eu-west-1', provider: 'aws', environment: 'dev',
  entity, deadLetterReason: 'Timeout', ruleId: 1, ruleName: 'Retry timeouts', reasonCode: 'PROVIDER_CANNOT_VERIFY_ABSENCE',
  reason: 'This cloud can’t prove a replayed message stayed fixed, so ServiceHub never replays here on its own. A person decides.', since: new Date().toISOString(),
})

const close = vi.fn()
function open() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}><MemoryRouter initialEntries={['/?modal=approve&group=aws%3An1']}><ApproveModal entry={{} as never} close={close} /></MemoryRouter></QueryClientProvider>)
}

describe('Approve and Decline', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(api.fetchPendingWork).mockResolvedValue({ items: [item('e1', 'orders-sqs'), item('e2', 'payments-sqs')], total: 2, byProvider: [], agents: 0 })
    vi.mocked(replay.fetchReplayProposal).mockResolvedValue({ checks: [{ id: 'verification', label: 'AWS can’t confirm the fix held', state: 'warning', detail: 'The result will read “verification required”' }] } as never)
    vi.mocked(api.approvePending).mockResolvedValue({} as never)
    vi.mocked(identity.fetchMe).mockResolvedValue({ ownerId: 'o', authMethod: 'session', actor: { identity: 'session', kind: 'user', label: 'from this browser session', isSession: true }, effectiveRole: 'Admin', governanceActive: false })
  })

  it('shows a Viewer the actions disabled with the reason and who can grant it — never hidden', async () => {
    vi.mocked(identity.fetchMe).mockResolvedValue({
      ownerId: 'o', authMethod: 'ApiKey', actor: { identity: 'ApiKey:reader', kind: 'apiKey', label: 'ApiKey:reader', isSession: false },
      effectiveRole: 'Viewer', governanceActive: true, grantors: ['ApiKey:lead'], recoverRole: 'Viewer', namespaceRecoverRoles: { n1: 'Viewer' },
    })
    open()
    expect(await screen.findByText(/you need the Approver role/)).toHaveTextContent('ApiKey:lead can grant it')
    expect(screen.getByRole('button', { name: /Approve 2 replays/ })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Decline…' })).toBeDisabled()
  })

  it('says why it asked in plain words, and approves only the ticked items', async () => {
    open()
    expect(await screen.findByText(/never replays here on its own/)).toBeInTheDocument()
    expect(await screen.findByText(/AWS can’t confirm the fix held/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('checkbox', { name: 'Include payments-sqs' }))
    await userEvent.click(screen.getByRole('button', { name: /Approve 1 replay/ }))
    expect(api.approvePending).toHaveBeenCalledTimes(1)
    expect(api.approvePending).toHaveBeenCalledWith('e1')
    expect(await screen.findByText(/1 replay sent, each recorded with your name/)).toBeInTheDocument()
  })

  it('declines only with a reason', async () => {
    open()
    await userEvent.click(await screen.findByRole('button', { name: 'Decline…' }))
    const confirm = screen.getByRole('button', { name: 'Decline 2' })
    expect(confirm).toBeDisabled()
    await userEvent.type(screen.getByLabelText(/Why not/), 'Downstream still down')
    await userEvent.click(confirm)
    expect(api.declinePending).toHaveBeenCalledWith('e1', 'Downstream still down')
    expect(api.declinePending).toHaveBeenCalledWith('e2', 'Downstream still down')
  })

  it('"Not now" changes nothing', async () => {
    open()
    await userEvent.click(await screen.findByRole('button', { name: 'Not now' }))
    expect(close).toHaveBeenCalled()
    expect(api.approvePending).not.toHaveBeenCalled()
    expect(api.declinePending).not.toHaveBeenCalled()
  })
})
