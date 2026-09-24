import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { ReplayListItem, ReplayVerification } from '../lib/api/replay'
import { WatchCard } from './agent/WatchCard'
import { OutcomeCard } from './OutcomeCard'

const replay = (verification: Partial<ReplayVerification>, over: Partial<ReplayListItem> = {}): ReplayListItem => ({
  id: 1, dlqMessageId: 7, namespaceId: 'n', provider: 'azure', messageId: 'm', sourceEntity: 'payments-dlq', targetEntity: 'payments',
  replayedAt: '2026-09-25T10:00:00Z', replayedBy: 'session', actor: { identity: 'session', kind: 'user', label: 'from this browser session', isSession: true }, outcomeStatus: 'accepted', entryState: 'Recovered', observationWindowEndsAt: null, markerApplied: true,
  verification: { status: 'verified', reasonCode: null, confidence: null, watchUntil: '2026-09-26T10:00:00Z', canConfirm: true, remedy: null, ...verification }, ...over,
})

const now = new Date('2026-09-25T10:10:00Z')

describe('the outcome card', () => {
  it('says Verified only in its verified state', () => {
    render(<OutcomeCard replay={replay({})} now={now} />)
    expect(screen.getByText('Verified')).toBeInTheDocument()
  })

  it('says Verification required — with the remedy — and never implies the message was not replayed', () => {
    render(<OutcomeCard replay={replay({ status: 'verification_required', canConfirm: false, remedy: 'SETUP_DLQ_OBSERVER' }, { provider: 'aws' })} now={now} />)
    expect(screen.getByText('Verification required')).toBeInTheDocument()
    expect(screen.getByText(/set up the dead-letter observer/i)).toBeInTheDocument()
    expect(screen.getByText(/may well have worked/)).toBeInTheDocument()
    expect(screen.queryByText(/not replayed|failed to replay/i)).toBeNull()
    expect(screen.queryByText('Verified')).toBeNull()
  })

  it('says a return came back, how it was matched, and that nothing retries it', () => {
    render(<OutcomeCard replay={replay({ status: 'returned', confidence: 'Heuristic' })} now={now} />)
    expect(screen.getByText('Came back')).toBeInTheDocument()
    expect(screen.getByText(/matched by its contents/)).toBeInTheDocument()
    expect(screen.getByText(/Nothing retries it/)).toBeInTheDocument()
  })

  it('has the same shape on every cloud: a status line, the cloud and the age', () => {
    const { container: a } = render(<OutcomeCard replay={replay({})} now={now} />)
    const { container: b } = render(<OutcomeCard replay={replay({ status: 'verification_required', canConfirm: false }, { provider: 'aws' })} now={now} />)
    for (const c of [a, b]) {
      expect(c.querySelectorAll('section')).toHaveLength(1)
      expect(c.querySelectorAll('p').length).toBeGreaterThanOrEqual(3)
    }
  })
})

describe('the watch card', () => {
  it('promises "confirmed fixed" only where the cloud can prove it', () => {
    render(<WatchCard replay={replay({ status: 'watching', canConfirm: true })} />)
    expect(screen.getByText(/it’s confirmed fixed/)).toBeInTheDocument()
  })

  it('tells the truth in advance where the cloud cannot', () => {
    render(<WatchCard replay={replay({ status: 'watching', canConfirm: false, remedy: 'SETUP_DLQ_OBSERVER' }, { provider: 'aws' })} />)
    expect(screen.queryByText(/it’s confirmed fixed/)).toBeNull()
    expect(screen.getByText(/not “Verified”/)).toBeInTheDocument()
  })
})
