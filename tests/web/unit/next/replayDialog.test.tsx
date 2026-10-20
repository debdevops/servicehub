import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as agentsApi from '@/lib/api/agents'
import * as componentsApi from '@/lib/api/components'
import * as coverageApi from '@/lib/api/coverage'
import * as deadLettersApi from '@/lib/api/deadLetters'
import * as identityApi from '@/lib/api/identity'
import * as namespacesApi from '@/lib/api/namespaces'
import * as pendingApi from '@/lib/api/pendingWork'
import * as replayApi from '@/lib/api/replay'
import * as settingsApi from '@/lib/api/settings'
import { nextRoute } from '@/next/router'
import { expectNoAxeViolations } from '@tests/support/axe'
import { agent, component, coverageRows, me, page } from './homeData'

vi.mock('@/lib/api/coverage', async (o) => ({ ...(await o<typeof coverageApi>()), fetchCoverage: vi.fn() }))
vi.mock('@/lib/api/components', async (o) => ({ ...(await o<typeof componentsApi>()), fetchComponents: vi.fn() }))
vi.mock('@/lib/api/identity', async (o) => ({ ...(await o<typeof identityApi>()), fetchMe: vi.fn() }))
vi.mock('@/lib/api/agents', async (o) => ({ ...(await o<typeof agentsApi>()), fetchAgents: vi.fn() }))
vi.mock('@/lib/api/settings', async (o) => ({ ...(await o<typeof settingsApi>()), fetchEmergencyStop: vi.fn() }))
vi.mock('@/lib/api/pendingWork', async (o) => ({ ...(await o<typeof pendingApi>()), fetchPendingWork: vi.fn() }))
vi.mock('@/lib/api/namespaces', async (o) => ({ ...(await o<typeof namespacesApi>()), fetchNamespaces: vi.fn(), fetchNamespace: vi.fn() }))
vi.mock('@/lib/api/deadLetters', async (o) => ({ ...(await o<typeof deadLettersApi>()), fetchDeadLetter: vi.fn() }))
vi.mock('@/lib/api/replay', async (o) => ({ ...(await o<typeof replayApi>()), fetchReplayProposal: vi.fn(), replayMessage: vi.fn(), fetchReplays: vi.fn() }))

const at = '2026-10-18T10:00:00Z'
const proposal = (over: Partial<replayApi.ReplayProposal> = {}): replayApi.ReplayProposal => ({
  dlqMessageId: 7, messageId: 'm-7', sourceEntity: 'payments-dlq', targetEntity: 'payments', namespaceName: 'orders-sb', provider: 'azure', environment: 'Dev', stampsRecoveryMarker: true,
  priorAttempts: 0, attemptCap: 3, othersLikeIt: 0, observationWindowHours: 1, canConfirm: true, verdict: 'Allow', reasonCode: null, approvable: false, canExecute: true, blockedCode: null,
  checks: [{ id: 'status', label: 'Still in the dead-letter queue', state: 'passed', detail: null }], ...over,
})
const outcome: replayApi.ReplayOutcome = { entryId: 'e1', operationId: 'o1', result: 'accepted', state: 'Observing', markerApplied: true, observationWindowEndsAt: at, message: 'Sent back to payments.', errorCode: null }

function open(url: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter([nextRoute], { initialEntries: [url] })
  return { router, ...render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>) }
}

describe('Replay dialog (unit F2)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(coverageApi.fetchCoverage).mockResolvedValue({ rows: coverageRows })
    vi.mocked(componentsApi.fetchComponents).mockResolvedValue({ components: [component()], generatedAtUtc: at })
    vi.mocked(identityApi.fetchMe).mockResolvedValue(me())
    vi.mocked(agentsApi.fetchAgents).mockResolvedValue([agent()])
    vi.mocked(settingsApi.fetchEmergencyStop).mockResolvedValue({ active: false, by: null, at: null, reason: null })
    vi.mocked(pendingApi.fetchPendingWork).mockResolvedValue(page([]))
    vi.mocked(namespacesApi.fetchNamespaces).mockResolvedValue([])
    vi.mocked(replayApi.fetchReplays).mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 25 })
    vi.mocked(replayApi.fetchReplayProposal).mockResolvedValue(proposal())
    vi.mocked(deadLettersApi.fetchDeadLetter).mockResolvedValue({
      item: { id: 7, messageId: 'm-7', namespaceId: 'ns-azure', entityName: 'payments', entityType: 'queue', topicName: null, detectedAtUtc: at, enqueuedTimeUtc: at, sequenceNumber: 7, sizeInBytes: 10, deadLetterReason: 'Timeout', deadLetterErrorDescription: 'gateway timeout', deliveryCount: 3, status: 'active' },
      bodyPreview: '{"a":1}', bodyIsPreview: false, othersLikeIt: 0, resolvedAt: null, correlationId: null, sessionId: null, contentType: null, applicationPropertiesJson: null,
    } as never)
  })

  it('reads the message from ?dialog=replay&id= and shows the proposal BEFORE anything is sent', async () => {
    open('/next/azure?dialog=replay&id=7')
    const dialog = await screen.findByRole('dialog')
    await waitFor(() => expect(replayApi.fetchReplayProposal).toHaveBeenCalledWith(7))
    // The 4.2.0 dialog repeats the one button at the top and the foot, so a long list of checks never hides it.
    for (const b of await within(dialog).findAllByRole('button', { name: 'Replay 1 message' })) expect(b).toBeEnabled()
    expect(replayApi.replayMessage).not.toHaveBeenCalled()
  })

  it('sends exactly one replay on the one click, then shows the honest ending with this shell’s links', async () => {
    const user = userEvent.setup()
    vi.mocked(replayApi.replayMessage).mockResolvedValue(outcome)
    open('/next/azure?dialog=replay&id=7')
    await user.click((await screen.findAllByRole('button', { name: 'Replay 1 message' }))[0])
    expect(await screen.findByText('Sent back')).toBeInTheDocument()
    expect(replayApi.replayMessage).toHaveBeenCalledTimes(1)
    expect(replayApi.replayMessage).toHaveBeenCalledWith(7)
    const dialog = screen.getByRole('dialog')
    expect(within(dialog).getByRole('link', { name: 'Replayed' })).toHaveAttribute('href', '/next/azure/replays')
    expect(within(dialog).getByRole('link', { name: 'Active messages' })).toHaveAttribute('href', '/next/azure/active')
  })

  it('keeps the button, disabled, when replay is blocked — it is never hidden', async () => {
    vi.mocked(replayApi.fetchReplayProposal).mockResolvedValue(proposal({ canExecute: false, verdict: 'Deny', blockedCode: 'EMERGENCY_STOP_ACTIVE', checks: [{ id: 'stop', label: 'Emergency stop is off', state: 'blocked', detail: null }] }))
    open('/next/azure?dialog=replay&id=7')
    for (const b of await screen.findAllByRole('button', { name: 'Replay 1 message' })) expect(b).toBeDisabled()
  })

  it('does not replay, and says so, when the proposal cannot be worked out', async () => {
    vi.mocked(replayApi.fetchReplayProposal).mockRejectedValue(new Error('boom'))
    open('/next/azure?dialog=replay&id=7')
    expect(await screen.findByRole('alert')).toHaveTextContent('so it won’t replay')
    expect(screen.queryByRole('button', { name: 'Replay 1 message' })).not.toBeInTheDocument()
  })

  it('offers Replay from the Message dialog only for a message still in dead-letter that nothing is holding', async () => {
    const { router } = open('/next/azure?dialog=message&id=7')
    const link = await screen.findByRole('link', { name: 'Replay…' })
    expect(link).toHaveAttribute('href', '/next/azure?dialog=replay&id=7')
    expect(router.state.location.search).toContain('dialog=message')
  })

  it('has no accessibility violations', async () => {
    open('/next/azure?dialog=replay&id=7')
    const dialog = await screen.findByRole('dialog')
    await within(dialog).findAllByRole('button', { name: 'Replay 1 message' })
    await expectNoAxeViolations(dialog)
  })
})
