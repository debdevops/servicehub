import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as dl from '../../lib/api/deadLetters'
import * as replayApi from '../../lib/api/replay'
import type { ReplayOutcome, ReplayProposal } from '../../lib/api/replay'
import * as identity from '../../lib/api/identity'
import ReplayModal from './ReplayModal'
import { expectNoAxeViolations } from '../../test/axe'

vi.mock('../../lib/api/deadLetters')
vi.mock('../../lib/api/replay')
vi.mock('../../lib/api/identity')
const proposalMock = vi.mocked(replayApi.fetchReplayProposal)
const replayMock = vi.mocked(replayApi.replayMessage)

const proposal = (over: Partial<ReplayProposal> = {}): ReplayProposal => ({
  dlqMessageId: 7, messageId: 'm-7', sourceEntity: 'payments-dlq', targetEntity: 'payments', namespaceName: 'orders-dev', provider: 'azure',
  environment: 'Dev', stampsRecoveryMarker: true, priorAttempts: 0, attemptCap: 3, othersLikeIt: 0, observationWindowHours: 24, canConfirm: true,
  verdict: 'Allow', reasonCode: null, approvable: false, canExecute: true, blockedCode: null,
  checks: [
    { id: 'status', label: 'Still in the dead-letter queue', state: 'passed', detail: null },
    { id: 'environment', label: 'Not a production namespace', state: 'passed', detail: 'Dev' },
    { id: 'frequency', label: 'Not replayed too often', state: 'passed', detail: '0 of 3 earlier attempts' },
    { id: 'verification', label: 'The cloud can confirm whether it stayed fixed', state: 'passed', detail: null },
  ], ...over,
})

const outcome = (over: Partial<ReplayOutcome> = {}): ReplayOutcome => ({
  entryId: 'e1', operationId: 'o1', result: 'accepted', state: 'Observing', markerApplied: true, observationWindowEndsAt: '2026-09-26T10:00:00Z',
  message: 'Sent back to payments. ServiceHub will watch for it coming back.', errorCode: null, ...over,
})

function renderModal() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const close = vi.fn()
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/?tab=dlq&message=7&modal=replay']}>
        <ReplayModal entry={{} as never} close={close} />
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return close
}

describe('the replay proposal', () => {
  beforeEach(() => {
    vi.mocked(dl.fetchDeadLetter).mockResolvedValue({
      item: { id: 7, deadLetterReason: 'ValidationFailed', deadLetterErrorDescription: 'missing required field: customerId', deliveryCount: 3 },
    } as never)
    vi.mocked(identity.fetchMe).mockResolvedValue({ ownerId: 'o', authMethod: 'session', effectiveRole: null, actor: { identity: 'session', kind: 'user', label: 'from this browser session', isSession: true } })
    proposalMock.mockReset()
    replayMock.mockReset()
  })

  it('has no accessibility violations (6.6)', async () => {
    renderModal()
    await new Promise((r) => setTimeout(r, 150))
    await expectNoAxeViolations(document.body)
  })

  it('says what will happen and lists the checks before anything runs', async () => {
    proposalMock.mockResolvedValue(proposal())
    renderModal()
    expect(await screen.findByText('What will happen')).toBeInTheDocument()
    expect(screen.getByText(/1 message is sent back to/)).toBeInTheDocument()
    expect(screen.getByText(/all passed/)).toBeInTheDocument()
    expect(screen.getAllByRole('listitem')).toHaveLength(4)
    expect(replayMock).not.toHaveBeenCalled()
  })

  it('warns it may fail the same way again when the error names a missing field', async () => {
    proposalMock.mockResolvedValue(proposal({ othersLikeIt: 6 }))
    renderModal()
    expect(await screen.findByText(/It may fail the same way again/)).toBeInTheDocument()
    expect(screen.getByText('customerId')).toBeInTheDocument()
    expect(screen.getByText(/6 other messages/)).toBeInTheDocument()
  })

  it('does not fabricate a "verified" claim where the cloud cannot prove it', async () => {
    proposalMock.mockResolvedValue(proposal({ canConfirm: false, provider: 'aws', checks: [{ id: 'verification', label: 'The cloud cannot prove it stayed fixed', state: 'warning', detail: null }] }))
    renderModal()
    expect(await screen.findByText(/verification required/)).toBeInTheDocument()
    expect(screen.getByText(/1 to look at/)).toBeInTheDocument()
  })

  it('keeps Replay, disabled, with the gate reason code beside it when blocked', async () => {
    proposalMock.mockResolvedValue(proposal({ canExecute: false, verdict: 'Deny', reasonCode: 'PRODUCTION_ELEVATION_REQUIRED', blockedCode: 'PRODUCTION_ELEVATION_REQUIRED' }))
    renderModal()
    expect(await screen.findByRole('button', { name: /Replay 1 message/ })).toBeDisabled()
    expect(screen.getByText('PRODUCTION_ELEVATION_REQUIRED')).toBeInTheDocument()
  })

  it('replays once on click and then says it is not verified yet', async () => {
    proposalMock.mockResolvedValue(proposal())
    replayMock.mockResolvedValue(outcome())
    const user = userEvent.setup()
    renderModal()
    await user.click(await screen.findByRole('button', { name: /Replay 1 message/ }))
    expect(await screen.findByText('Sent back')).toBeInTheDocument()
    expect(screen.getByText(/ServiceHub will say whether it stayed fixed/)).toBeInTheDocument()
    expect(replayMock).toHaveBeenCalledTimes(1)
    expect(replayMock).toHaveBeenCalledWith(7)
  })

  it('says plainly when the outcome is unknown, and warns about a duplicate', async () => {
    proposalMock.mockResolvedValue(proposal())
    replayMock.mockResolvedValue(outcome({ result: 'unknown', state: 'ExecutionUnknown', message: 'ServiceHub lost contact with the cloud.' }))
    const user = userEvent.setup()
    renderModal()
    await user.click(await screen.findByRole('button', { name: /Replay 1 message/ }))
    expect(await screen.findByText('Outcome unknown')).toBeInTheDocument()
    expect(screen.getByText(/second copy may be sent/)).toBeInTheDocument()
  })

  it('refuses to guess when the proposal cannot be worked out', async () => {
    proposalMock.mockRejectedValue(new Error('boom'))
    renderModal()
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent(/won’t replay/))
  })
})
