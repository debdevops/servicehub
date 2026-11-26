import { describe, expect, it } from 'vitest'
import { readsOnDemand, type DetectionKind } from '@/lib/api/coverage'
import { failedWords, itemStatusTone, itemStatusWords } from '@/next/lib/failedWords'
import { handOffNote, redriveWords } from '@/next/lib/redriveWords'

describe('which components are read on demand', () => {
  it.each<[DetectionKind, boolean]>([['runs', true], ['records', true], ['store', false], ['signal', false], ['none', false]])('%s → %s', (detection, expected) => {
    expect(readsOnDemand(detection)).toBe(expected)
  })
})

describe('the words of the failed-items tab', () => {
  it('speaks of runs for a run history', () => {
    const w = failedWords('runs')
    expect(w.title).toBe('Failed runs')
    expect(w.one).toBe('run')
    expect(w.empty('Logic Apps', false)).toBe('No failed, timed-out or aborted Logic Apps runs were found.')
    expect(w.empty('Logic Apps', true)).toBe('No failed, timed-out or aborted Logic Apps runs were found in what could be read.')
    expect(w.caption('Logic Apps')).toBe('Failed Logic Apps runs, newest first')
    expect(w.foot('Logic Apps')).toContain('To run one again')
    expect([w.first, w.last]).toEqual(['Started', 'Stopped'])
    expect(w.gone).toContain('run')
  })

  it('says Redrive… exists, and stops saying nothing moves on, only where the row offers a cloud redrive', () => {
    const w = failedWords('runs')
    const note = redriveWords('step-functions').footNote
    expect(w.foot('Step Functions', note)).toContain('Redrive…')
    expect(w.foot('Step Functions', note)).not.toMatch(/does not move work on/)
    expect(w.foot('Step Functions', undefined)).toContain('does not move work on for Step Functions')
    expect(w.foot('Step Functions')).toContain('does not move work on for Step Functions')
  })

  it('says Resubmit… and that every step runs again for Logic Apps, and never calls it a redrive', () => {
    const w = failedWords('runs')
    const text = w.foot('Logic Apps', redriveWords('logic-apps').footNote)
    expect(text).toContain('Resubmit…')
    expect(text).toContain('every step runs again')
    expect(text).not.toMatch(/Redrive|from the step that failed/)
  })

  it('says where a held record can be retried, or why nowhere, instead of the generic line', () => {
    const w = failedWords('records')
    const eventarc = w.foot('Eventarc', handOffNote('eventarc'))
    const eventGrid = w.foot('Event Grid', handOffNote('event-grid'))
    expect(eventarc).toContain('Retry in Pub/Sub')
    expect(eventGrid).toContain('ServiceHub cannot retry these')
    expect(eventarc).not.toMatch(/does not move work on/)
    expect(eventGrid).not.toMatch(/does not move work on/)
    expect(w.foot('Kinesis')).toContain('does not move work on for Kinesis')
  })

  it('speaks of records for failed records, and never promises a re-run', () => {
    const w = failedWords('records')
    expect(w.title).toBe('Failed records')
    expect(w.one).toBe('record')
    expect(w.empty('Event Grid', true)).toBe('No failed or undelivered Event Grid records were found in what could be read.')
    expect(w.caption('Event Grid')).toBe('Failed Event Grid records, newest first')
    expect(w.foot('Event Grid')).toContain('does not move work on for Event Grid')
    expect(w.foot('Event Grid')).not.toMatch(/run one again/)
    expect([w.first, w.last]).toEqual(['Earliest', 'Latest'])
    expect(w.gone).toContain('record')
  })

  it('uses a runs wording for anything that is not records, so a new kind never shows blank words', () => {
    expect(failedWords('store')).toBe(failedWords('runs'))
    expect(failedWords('signal')).toBe(failedWords('runs'))
  })

  it('gives each tab its own help keys so the (i) never explains the wrong thing', () => {
    expect(failedWords('runs').tip).not.toBe(failedWords('records').tip)
    expect(failedWords('runs').detailTip).not.toBe(failedWords('records').detailTip)
  })
})

describe('the cloud’s own status words', () => {
  it.each([
    ['FAILED', 'Failed'], ['TIMED_OUT', 'Timed out'], ['ABORTED', 'Aborted'], ['CANCELLED', 'Cancelled'],
    ['Failed', 'Failed'], ['TimedOut', 'Timed out'], ['Cancelled', 'Cancelled'],
    ['DEAD_LETTERED', 'Dead-lettered'], ['UNDELIVERED', 'Undelivered'],
  ])('%s is shown as %s', (state, words) => {
    expect(itemStatusWords[state]).toBe(words)
    expect(itemStatusTone[state]).toBeTruthy()
  })

  it('has a tone for every word it has, so no status is drawn unstyled', () => {
    expect(Object.keys(itemStatusWords).sort()).toEqual(Object.keys(itemStatusTone).sort())
  })

  it('leaves a state it does not know to be shown as the cloud wrote it', () => {
    expect(itemStatusWords.SOMETHING_NEW).toBeUndefined()
  })
})
