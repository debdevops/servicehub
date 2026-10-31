import { describe, expect, it } from 'vitest'
import type { LedgerEvent } from '@/lib/api/recovery'
import { describeEvent, meaningOf, stateMeaning } from '@/lib/ledgerWords'

const event = (eventType: string, detail: string | null = null) => ({ eventType, detail }) as unknown as LedgerEvent

describe('ledger words for a redrive', () => {
  it('never describes a redrive in queue-replay words', () => {
    for (const state of ['Executing', 'ExecutionFailed', 'ExecutionUnknown', 'Unverified'] as const) {
      expect(meaningOf(state, 'Redrive')).not.toMatch(/replayed|dead-letter queue|queue stayed empty/)
    }
  })

  it('says what a redrive acceptance does and does not prove', () => {
    expect(meaningOf('Unverified', 'Redrive')).toBe('the cloud accepted the request, but ServiceHub cannot confirm the run then finished fixed')
  })

  it('keeps the replay words for a replay, a purge and an unknown kind', () => {
    for (const kind of ['Replay', 'Purge', undefined, 'Something']) {
      expect(meaningOf('Unverified', kind)).toBe(stateMeaning.Unverified)
      expect(meaningOf('ExecutionFailed', kind)).toBe(stateMeaning.ExecutionFailed)
    }
  })

  it('falls back to the shared words for a state a redrive has no words for', () => {
    expect(meaningOf('Recovered', 'Redrive')).toBe(stateMeaning.Recovered)
  })

  it('titles a redrive entry\'s events as a request to move the run on, true of a redrive and a resubmit alike', () => {
    expect(describeEvent(event('EntryBegun'), 'AWS', 'Redrive').title).toBe('Request to move the run on')
    expect(describeEvent(event('EntryBegun'), 'AWS').title).toBe('Replay begun')
    expect(describeEvent(event('ProviderAccepted'), 'AWS', 'Redrive').title).toBe('AWS accepted the request')
    expect(describeEvent(event('ProviderAccepted'), 'AWS').title).toBe('Sent, and accepted by AWS')
  })

  it('does not call a refused redrive "not found in the dead-letter queue"', () => {
    const refused = event('ProviderRejected', JSON.stringify({ errorCode: 'AWS.StepFunctions.NotFound' }))
    expect(describeEvent(refused, 'AWS', 'Redrive').title).toBe('AWS rejected it')
    expect(describeEvent(refused, 'AWS').title).toContain('dead-letter queue')
  })
})
