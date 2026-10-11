import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { choose } from '../../../support/choose'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as api from '@/lib/api/pendingWork'
import * as replay from '@/lib/api/replay'
import * as identity from '@/lib/api/identity'
import * as signatures from '@/lib/api/signatures'
import * as deadLetters from '@/lib/api/deadLetters'
import type { PendingWorkItem } from '@/lib/api/pendingWork'
import ApproveModal from '@/components/approve/ApproveModal'
import { OverlayTitleContext } from '@/components/overlays/overlayTitle'
import { expectNoAxeViolations } from '@tests/support/axe'

vi.mock('@/lib/api/pendingWork', async (original) => ({ ...(await original<typeof api>()), fetchPendingWork: vi.fn(), approvePending: vi.fn(), declinePending: vi.fn(), resolvePending: vi.fn() }))
vi.mock('@/lib/api/replay', async (original) => ({ ...(await original<typeof replay>()), fetchReplayProposal: vi.fn() }))
vi.mock('@/lib/api/signatures', async (original) => ({ ...(await original<typeof signatures>()), fetchSignatures: vi.fn() }))
vi.mock('@/lib/api/deadLetters', async (original) => ({ ...(await original<typeof deadLetters>()), fetchDeadLetter: vi.fn() }))
vi.mock('@/lib/api/identity', async (original) => ({ ...(await original<typeof identity>()), fetchMe: vi.fn() }))

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

describe('An attempt whose answer was lost', () => {
  const unresolved: PendingWorkItem = {
    ...item('u1', 'orders'), kind: 'unresolved', ruleId: null, ruleName: null, provider: 'azure', reasonCode: 'REPLAY_OUTCOME_UNKNOWN',
    reason: 'ServiceHub stopped before it could record whether an earlier attempt put this message back. Check the queue, then say what you found.',
  }
  function openUnresolved() {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    return render(<QueryClientProvider client={client}><MemoryRouter initialEntries={['/?modal=approve&entry=u1']}><ApproveModal entry={{} as never} close={close} /></MemoryRouter></QueryClientProvider>)
  }
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(api.fetchPendingWork).mockResolvedValue({ items: [unresolved], total: 1, byProvider: [], agents: 0 })
    vi.mocked(api.resolvePending).mockResolvedValue(undefined)
    vi.mocked(identity.fetchMe).mockResolvedValue({ ownerId: 'o', authMethod: 'session', actor: { identity: 'session', kind: 'user', label: 'from this browser session', isSession: true }, governanceActive: false } as never)
  })

  it('asks what the person found, never offers to approve a replay, and records the answer in their words', async () => {
    openUnresolved()
    expect(await screen.findByText(/Check the queue/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Approve/ })).not.toBeInTheDocument()

    const record = screen.getByRole('button', { name: 'Record what I found' })
    expect(record).toBeDisabled() // a reason is required: ServiceHub does not know the outcome, so it will not make one up
    await userEvent.type(screen.getByLabelText(/What did you find/), 'the main queue holds one copy')
    await userEvent.click(record)

    await waitFor(() => expect(api.resolvePending).toHaveBeenCalledWith('u1', 'the main queue holds one copy'))
    expect(await screen.findByText(/closed without a verdict/)).toBeInTheDocument()
  })

  it('retitles its own frame, so a question with nothing to approve is never headed "Approve"', async () => {
    const retitle = vi.fn()
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(<QueryClientProvider client={client}><MemoryRouter initialEntries={['/?modal=approve&entry=u1']}><OverlayTitleContext.Provider value={retitle}><ApproveModal entry={{} as never} close={close} /></OverlayTitleContext.Provider></MemoryRouter></QueryClientProvider>)
    await screen.findByText(/Check the queue/)
    expect(retitle).toHaveBeenCalledWith(expect.objectContaining({ title: 'Say what happened' }))
  })

  it('still says it was recorded when the list refreshes and the item is gone (found in a live run)', async () => {
    // The real list drops the item the moment the answer is recorded; the confirmation must not vanish with it.
    let recorded = false
    vi.mocked(api.fetchPendingWork).mockImplementation(async () => (recorded ? { items: [], total: 0, byProvider: [], agents: 0 } : { items: [unresolved], total: 1, byProvider: [], agents: 0 }))
    vi.mocked(api.resolvePending).mockImplementation(async () => { recorded = true })
    openUnresolved()
    await userEvent.type(await screen.findByLabelText(/What did you find/), 'nothing was sent')
    await userEvent.click(screen.getByRole('button', { name: 'Record what I found' }))

    expect(await screen.findByText(/Recorded with your name/)).toBeInTheDocument()
    await waitFor(() => expect(vi.mocked(api.fetchPendingWork).mock.calls.length).toBeGreaterThan(1)) // the list was refreshed after the answer
    expect(screen.getByText(/Recorded with your name/)).toBeInTheDocument()
    expect(screen.queryByText(/Nothing here is waiting any more/)).not.toBeInTheDocument()
  })

  it('says so, and records nothing, when the answer cannot be saved', async () => {
    vi.mocked(api.resolvePending).mockRejectedValueOnce(new Error('down'))
    openUnresolved()
    await userEvent.type(await screen.findByLabelText(/What did you find/), 'nothing was sent')
    await userEvent.click(screen.getByRole('button', { name: 'Record what I found' }))
    expect(await screen.findByRole('alert')).toHaveTextContent(/Nothing was recorded/)
  })
})

describe('Approve and Decline', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(api.fetchPendingWork).mockResolvedValue({ items: [item('e1', 'orders-sqs'), item('e2', 'payments-sqs')], total: 2, byProvider: [], agents: 0 })
    vi.mocked(replay.fetchReplayProposal).mockResolvedValue({ checks: [{ id: 'verification', label: 'AWS can’t confirm the fix held', state: 'warning', detail: 'The result will read “verification required”' }] } as never)
    vi.mocked(signatures.fetchSignatures).mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 50, all: 0, growing: 0, replayHelps: 0, replayDoesNotHelp: 0 })
    vi.mocked(deadLetters.fetchDeadLetter).mockRejectedValue(new Error('none'))
    vi.mocked(api.approvePending).mockResolvedValue({} as never)
    vi.mocked(identity.fetchMe).mockResolvedValue({ ownerId: 'o', authMethod: 'session', actor: { identity: 'session', kind: 'user', label: 'from this browser session', isSession: true }, effectiveRole: 'Admin', governanceActive: false })
  })

  it('has no accessibility violations (6.6)', async () => {
    open()
    await new Promise((r) => setTimeout(r, 150))
    await expectNoAxeViolations(document.body, { isolatedComponent: true })
  })

  it('when the safety checks cannot be read it says so and offers Try again, instead of showing none (6.1)', async () => {
    vi.mocked(replay.fetchReplayProposal).mockRejectedValueOnce(new Error('down'))
    open()
    await userEvent.click(await screen.findByRole('tab', { name: 'Safety checks' }))
    expect(await screen.findByText(/couldn’t show the safety checks just now/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }))
    expect(await screen.findByText(/AWS can’t confirm the fix held/)).toBeInTheDocument()
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
    await userEvent.click(screen.getByRole('tab', { name: 'Safety checks' }))
    expect(await screen.findByText(/AWS can’t confirm the fix held/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('tab', { name: /^Messages/ }))
    await userEvent.click(screen.getByRole('checkbox', { name: 'Include payments-sqs' }))
    await userEvent.click(screen.getByRole('button', { name: /Approve 1 replay/ }))
    expect(api.approvePending).toHaveBeenCalledTimes(1)
    expect(api.approvePending).toHaveBeenCalledWith('e1')
    const sent = await screen.findByText(/1 replay sent, each recorded with your name/)
    expect(sent.closest('[role=status]')).not.toBeNull() // the result is announced, not only drawn (6.6)
  })

  it('pages the waiting messages 10 at a time, lets you choose 20 or 50, and select all covers every page', async () => {
    const many = Array.from({ length: 30 }, (_, i) => ({ ...item(`e${i}`, 'orders-sqs'), dlqMessageId: null }))
    vi.mocked(api.fetchPendingWork).mockResolvedValue({ items: many, total: 30, byProvider: [], agents: 0 })
    open()
    expect(await screen.findByText('1–10 of 30')).toBeInTheDocument()
    expect(screen.getAllByRole('checkbox', { name: 'Include orders-sqs' })).toHaveLength(10)
    expect(screen.getByRole('button', { name: /Approve 30 replays/ })).toBeEnabled()

    await userEvent.click(screen.getByRole('checkbox', { name: 'Select all 30 waiting replays' }))
    expect(screen.getByRole('button', { name: /Approve 0 replays/ })).toBeDisabled()
    await userEvent.click(screen.getByRole('checkbox', { name: 'Select all 30 waiting replays' }))
    expect(screen.getByRole('button', { name: /Approve 30 replays/ })).toBeEnabled()

    await userEvent.click(screen.getByRole('button', { name: 'Next page' }))
    expect(screen.getByText('11–20 of 30')).toBeInTheDocument()
    expect(screen.getByText('Page 2 of 3')).toBeInTheDocument()
    await choose(screen.getByLabelText('Rows per page'), '20')
    expect(screen.getByText('1–20 of 30')).toBeInTheDocument()
    expect(screen.getAllByRole('checkbox', { name: 'Include orders-sqs' })).toHaveLength(20)
  })

  it('opens the message when its row is clicked, and shows the body laid out, then the properties and the delivery facts', async () => {
    vi.mocked(deadLetters.fetchDeadLetter).mockResolvedValue({
      item: { id: 7, namespaceId: 'n1', messageId: 'msg-abc', sequenceNumber: 1, entityName: 'orders-sqs', entityType: 'queue', topicName: null,
        detectedAtUtc: new Date().toISOString(), enqueuedTimeUtc: new Date().toISOString(), deliveryCount: 10, sizeInBytes: 295,
        deadLetterReason: 'Timeout', deadLetterErrorDescription: 'Took too long', status: 'active' },
      bodyPreview: '{"orderId":"ORD-1","amount":12.5}', bodyIsPreview: false, contentType: 'application/json', correlationId: 'corr-1',
      sessionId: null, applicationPropertiesJson: '{"shs-run-id":"abc"}', resolvedAt: null, othersLikeIt: 0,
    })
    open()
    const panel = await screen.findByRole('complementary', { name: 'Message details' })
    // Body — indented, one field to a line, not the stored single line.
    await waitFor(() => expect(panel).toHaveTextContent('"orderId": "ORD-1"'))
    expect(panel).toHaveTextContent('msg-abc')
    expect(panel).toHaveTextContent('corr-1')

    await userEvent.click(within(panel).getByRole('tab', { name: 'properties' }))
    expect(panel).toHaveTextContent('"shs-run-id": "abc"')

    await userEvent.click(within(panel).getByRole('tab', { name: 'delivery' }))
    expect(panel).toHaveTextContent('10 times')
    expect(panel).toHaveTextContent('295 B')
    expect(panel).toHaveTextContent('Took too long')

    // A click anywhere on the second row moves the panel to that message.
    await userEvent.click(screen.getAllByRole('row')[2])
    expect(deadLetters.fetchDeadLetter).toHaveBeenCalled()
  })

  it('shows a progress bar while the replays go out, so a long batch never looks frozen', async () => {
    let release: () => void = () => {}
    vi.mocked(api.approvePending).mockImplementation(() => new Promise((r) => { release = () => r({} as never) }))
    open()
    await userEvent.click(await screen.findByRole('button', { name: /Approve 2 replays/ }))
    const bar = await screen.findByRole('progressbar', { name: 'Approval progress' })
    expect(bar).toHaveAttribute('aria-valuenow', '0')
    expect(screen.getByText('0 of 2 sent')).toBeInTheDocument()
    release()
    await waitFor(() => expect(screen.getByText('1 of 2 sent')).toBeInTheDocument())
    release()
    expect(await screen.findByText(/2 replays sent, each recorded with your name/)).toBeInTheDocument()
  })

  it('declines only with a reason, says plainly what declining does, and then says what happened', async () => {
    open()
    await userEvent.click(await screen.findByRole('button', { name: 'Decline…' }))
    // The step replaces the footer, so it is always in view — not a box hidden further down the page.
    const step = screen.getByRole('region', { name: 'Decline these replays' })
    expect(step).toHaveTextContent('Decline 2 replays?')
    expect(step).toHaveTextContent('Nothing is replayed and nothing is deleted')
    expect(step).toHaveTextContent('Your reason and your name are recorded')
    expect(screen.queryByRole('button', { name: /Approve 2 replays/ })).not.toBeInTheDocument()
    const confirm = screen.getByRole('button', { name: 'Decline 2 replays' })
    expect(confirm).toBeDisabled()
    await userEvent.type(screen.getByLabelText(/Why not/), 'Downstream still down')
    await userEvent.click(confirm)
    expect(api.declinePending).toHaveBeenCalledWith('e1', 'Downstream still down')
    expect(api.declinePending).toHaveBeenCalledWith('e2', 'Downstream still down')
    const done = await screen.findByText(/Declined 2 replays\./)
    expect(done.closest('[role=status]')).not.toBeNull() // and so is the decline (6.6)
    expect(screen.getByRole('status')).toHaveTextContent('still in the dead-letter queue')
  })

  it('Go back from Decline returns to Approve without changing anything', async () => {
    open()
    await userEvent.click(await screen.findByRole('button', { name: 'Decline…' }))
    await userEvent.click(screen.getByRole('button', { name: 'Go back' }))
    expect(screen.getByRole('button', { name: /Approve 2 replays/ })).toBeEnabled()
    expect(api.declinePending).not.toHaveBeenCalled()
  })

  it('"Not now" changes nothing', async () => {
    open()
    await userEvent.click(await screen.findByRole('button', { name: 'Not now' }))
    expect(close).toHaveBeenCalled()
    expect(api.approvePending).not.toHaveBeenCalled()
    expect(api.declinePending).not.toHaveBeenCalled()
  })
  it('shows the real number waiting when the server sends only the first part, and says what stays waiting', async () => {
    const some = Array.from({ length: 12 }, (_, i) => ({ ...item(`e${i}`, 'orders-sqs'), dlqMessageId: null }))
    vi.mocked(api.fetchPendingWork).mockResolvedValue({ items: some, total: 550, byProvider: [], agents: 0 })
    open()
    expect(await screen.findByText(/550 replays waiting \(showing the first 12\)/)).toBeInTheDocument()
    expect(screen.getByText(/538 other messages stay waiting/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Approve 12 replays/ })).toBeEnabled()
  })

  it('tells the reader what Approve does and does not do before they press it', async () => {
    open()
    const before = await screen.findByRole('region', { name: 'Before you approve' })
    expect(before).toHaveTextContent('Nothing is deleted')
    expect(before).toHaveTextContent('doesn’t earn this failure any trust')
    expect(before).toHaveTextContent('no earlier replay of this failure has been checked yet')
  })

  it('warns, with the real numbers, when replaying this failure has not worked before', async () => {
    vi.mocked(signatures.fetchSignatures).mockResolvedValue({
      items: [{ signatureHash: 'h', provider: 'aws', reason: 'Timeout', exampleError: null, entities: ['orders-sqs', 'payments-sqs'], messages: 40, activeNow: 40, firstSeenAt: '', lastSeenAt: '', daily: [], growing: false,
        replays: { replayed: 8, stayedFixed: 0, returned: 8, unverified: 0 }, replayVerdict: 'doesnt', namespaces: [] }],
      total: 1, page: 1, pageSize: 50, all: 1, growing: 0, replayHelps: 0, replayDoesNotHelp: 1,
    })
    open()
    expect(await screen.findByText(/0 of 8 earlier replays stayed fixed/)).toBeInTheDocument()
  })

  it('names the Agent and its rule as the one who asked, not this browser', async () => {
    open()
    const facts = await screen.findByLabelText('What is being asked')
    expect(facts).toHaveTextContent('Asked by')
    expect(facts).toHaveTextContent('The Agent')
    expect(facts).toHaveTextContent('Retry timeouts')
  })

  it('searches the waiting messages without un-ticking anything', async () => {
    open()
    await userEvent.type(await screen.findByRole('searchbox', { name: 'Search the waiting messages' }), 'payments')
    expect(screen.getAllByRole('checkbox', { name: /^Include / })).toHaveLength(1)
    expect(screen.getByRole('button', { name: /Approve 2 replays/ })).toBeEnabled()
  })
})
