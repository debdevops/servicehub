import { describe, expect, it } from 'vitest'
import type { Agent } from '@/lib/api/agents'
import type { ConnectedComponent, Count } from '@/lib/api/components'
import type { Me } from '@/lib/api/identity'
import { agentState, countWords, evidenceSentence, familyWords, homeTotals, lookingAt, recordedWords, roleWords, statusWords, stopState } from '@/next/lib/words'

const component = (over: Partial<ConnectedComponent> = {}): ConnectedComponent => ({
  namespaceId: 'n1', componentKey: 'service-bus', cloud: 'azure', name: 'orders-sb', componentName: 'Service Bus', environment: 'dev', canSend: true,
  reachable: true, lastError: null, watched: true, class: 'r1ReplayableProvable', deadLetters: { value: 4, kind: 'exact' }, recorded: null,
  needsYou: 0, approvals: 0, handOffs: 0, observedAtUtc: '2026-10-18T10:00:00Z', entities: [], ...over,
})

const agent = (over: Partial<Agent> = {}): Agent => ({
  id: 'auto-replay', name: 'Auto replay', purpose: '', kind: 'worker', authority: 'acts', canAct: true, cadenceSeconds: 30, notes: null, may: [], mayNot: [],
  health: 'healthy', late: false, isPaused: false, lastRunUtc: null, lastResult: null, lastFailure: null, consecutiveFailures: 0, ...over,
}) as Agent

describe('countWords', () => {
  it.each<[Count, string]>([
    [{ value: 12, kind: 'exact' }, '12'],
    [{ value: 8700, kind: 'approximate' }, '8,700 (approx)'],
    [{ value: null, kind: 'notReported' }, 'Can’t count here'],
    [{ value: null, kind: 'notRead' }, 'Not read'],
  ])('%j reads as %s', (count, words) => expect(countWords(count)).toBe(words))

  it('never draws a count the cloud did not report as 0', () => {
    expect(countWords({ value: null, kind: 'notReported' })).not.toContain('0')
  })
})

describe('recordedWords', () => {
  it('says nothing where the cloud is watched, "Not looked yet" before a look, and "(limit)" when the look stopped early', () => {
    expect(recordedWords(null)).toBe('—')
    expect(recordedWords({ messages: null, reachedLimit: false, atUtc: null })).toBe('Not looked yet')
    expect(recordedWords({ messages: 100, reachedLimit: true, atUtc: null })).toBe('100 (limit)')
    expect(recordedWords({ messages: 0, reachedLimit: false, atUtc: null })).toBe('0')
  })
})

describe('statusWords', () => {
  it('follows the plan: needs you, then can’t reach, otherwise nothing needs attention', () => {
    expect(statusWords({ needsYou: 2, reachable: true })).toEqual({ text: 'Needs you', tone: 'bad' })
    expect(statusWords({ needsYou: 0, reachable: false })).toEqual({ text: 'Can’t reach', tone: 'warn' })
    expect(statusWords({ needsYou: 0, reachable: true })).toEqual({ text: 'No items need attention', tone: 'ok' })
  })
})

describe('homeTotals', () => {
  it('sums one cloud’s numbers and keeps the approximation', () => {
    const t = homeTotals([
      component({ deadLetters: { value: 7840, kind: 'approximate' }, needsYou: 3, approvals: 2, handOffs: 1, watched: false, recorded: { messages: 200, reachedLimit: false, atUtc: null } }),
      component({ deadLetters: { value: 860, kind: 'approximate' }, needsYou: 1, approvals: 1, watched: false, recorded: { messages: 100, reachedLimit: false, atUtc: null } }),
    ])
    expect(t.first).toMatchObject({ label: 'In dead-letter now', value: 8700, approximate: true, recorded: 300, components: 2 })
    expect([t.needsYou, t.approvals, t.handOffs]).toEqual([4, 3, 1])
  })

  it('says "Dead letters recorded" when no component reports a number, and shows what was kept, not 0', () => {
    const t = homeTotals([
      component({ deadLetters: { value: null, kind: 'notReported' }, watched: false, recorded: { messages: 137, reachedLimit: false, atUtc: null } }),
    ])
    expect(t.first).toMatchObject({ label: 'Dead letters recorded', value: 137, recorded: null })
  })

  it('says it cannot read, not that nothing was recorded, when the read failed on a cloud that does count', () => {
    const t = homeTotals([component({ deadLetters: { value: null, kind: 'notRead' }, reachable: false })])
    expect(t.first).toMatchObject({ label: 'In dead-letter now', value: null, unreadable: true })
  })

  it('has no value at all for a cloud that reports none and nobody has looked at yet', () => {
    const t = homeTotals([component({ deadLetters: { value: null, kind: 'notReported' }, watched: false, recorded: { messages: null, reachedLimit: false, atUtc: null } })])
    expect(t.first).toMatchObject({ label: 'Dead letters recorded', value: null, unreadable: false })
  })

  it('is empty, not an error, with no components', () => {
    expect(homeTotals([]).first).toMatchObject({ value: null, components: 0 })
  })
})

describe('lookingAt', () => {
  it('says what is true of the numbers and names no cloud’s behaviour', () => {
    expect(lookingAt('Azure', [component()])).toContain('latest values read by ServiceHub')
    expect(lookingAt('AWS', [component({ deadLetters: { value: 1, kind: 'approximate' }, watched: false })])).toContain('approximate')
    expect(lookingAt('AWS', [component({ deadLetters: { value: 1, kind: 'approximate' }, watched: false })])).toContain('recorded only when you look')
    expect(lookingAt('Google Cloud', [component({ deadLetters: { value: null, kind: 'notReported' } })])).toContain('reports no message counts')
  })
})

describe('evidenceSentence', () => {
  const base = { status: 'supported' as const, note: null, offersReplay: true }
  it('chooses by class, never by cloud', () => {
    expect(evidenceSentence({ ...base, class: 'r1ReplayableProvable' })).toMatchObject({ tone: 'good' })
    expect(evidenceSentence({ ...base, class: 'r2ReplayableUnproven', note: 'Ends Unverified' })).toMatchObject({ tone: 'caution' })
    expect(evidenceSentence({ ...base, class: 'r2ReplayableUnproven', note: 'Ends Unverified' }).text).toContain('Ends Unverified.')
    expect(evidenceSentence({ ...base, status: 'beingValidated', class: 'r0Unknown' }).text).toContain('Not yet validated')
    expect(evidenceSentence({ ...base, class: 'r5DetectOnly', offersReplay: false })).toMatchObject({ tone: 'none' })
  })
})

describe('familyWords', () => {
  it('turns the wire word into words, shows an unknown one spaced out, and nothing for none', () => {
    expect(familyWords('outcomeUnknown')).toBe('Outcome unknown')
    expect(familyWords('policyBlocked')).toBe('Policy blocked')
    expect(familyWords('somethingNew')).toBe('Something New')
    expect(familyWords(null)).toBeNull()
    expect(familyWords(undefined)).toBeNull()
  })
})

describe('the header’s words', () => {
  const me = (over: Partial<Me>): Me => ({ ownerId: 'o', authMethod: 'session', actor: { identity: 'x', kind: 'User' }, effectiveRole: 'Admin', ...over }) as Me

  it('shows the recovery role, falling back to the fleet role, and nothing before it is read', () => {
    expect(roleWords(undefined)).toBeNull()
    expect(roleWords(me({ recoverRole: 'Approver', effectiveRole: 'Operator' }))).toBe('Approver')
    expect(roleWords(me({ recoverRole: undefined, effectiveRole: 'Operator' }))).toBe('Operator')
    expect(roleWords(me({ recoverRole: null, effectiveRole: null }))).toBe('No role')
  })

  it('reads the Agent from the agents that can act', () => {
    expect(agentState(undefined).status).toBe('unknown')
    expect(agentState([]).status).toBe('unknown')
    expect(agentState([agent()]).status).toBe('running')
    expect(agentState([agent({ isPaused: true })])).toMatchObject({ status: 'paused', title: 'Agent paused' })
    expect(agentState([agent(), agent({ id: 'b', name: 'Second', isPaused: true })])).toMatchObject({ status: 'partly-paused', detail: 'Second paused' })
    expect(agentState([agent({ canAct: false, isPaused: true })]).status).toBe('unknown')
  })

  it('shows an unreadable stop as ON, a loading one as neither, and a real one as it is', () => {
    expect(stopState(undefined, true)).toEqual({ kind: 'unreadable' })
    expect(stopState(undefined, false)).toEqual({ kind: 'loading' })
    expect(stopState({ active: false, by: null, at: null, reason: null }, false)).toEqual({ kind: 'off' })
    expect(stopState({ active: true, by: 'maya', at: 'x', reason: 'drill' }, false)).toEqual({ kind: 'on', by: 'maya', at: 'x', reason: 'drill' })
  })
})

import { cloudFacts, noTotalSentence } from '@/next/lib/words'

describe('noTotalSentence', () => {
  const azure = { name: 'Azure', components: [component()] }
  const aws = { name: 'AWS', components: [component({ cloud: 'aws', deadLetters: { value: 9, kind: 'approximate' } })] }
  const gcp = { name: 'Google Cloud', components: [component({ cloud: 'gcp', deadLetters: { value: null, kind: 'notReported' } })] }

  it('says nothing with fewer than two clouds: there is nothing to add up', () => {
    expect(noTotalSentence([])).toBeNull()
    expect(noTotalSentence([azure])).toBeNull()
  })

  it('names what each cloud’s numbers are, from the data', () => {
    expect(noTotalSentence([azure, aws, gcp])).toBe('Azure counts are exact, AWS counts are approximate, and Google Cloud reports none, so adding them would give a number that means nothing. Each cloud keeps its own.')
    expect(noTotalSentence([azure, aws])).toContain('Azure counts are exact and AWS counts are approximate')
  })

  it('says a cloud could not be read rather than calling its numbers exact', () => {
    const down = { name: 'Azure', components: [component({ deadLetters: { value: null, kind: 'notRead' }, reachable: false })] }
    expect(noTotalSentence([down, aws])).toContain('Azure could not be read')
  })
})

describe('cloudFacts', () => {
  const supported = (cls: 'r1ReplayableProvable' | 'r2ReplayableUnproven') => ({ class: cls, status: 'supported' as const })

  it('reads watched, counts and evidence from the components and the catalogue', () => {
    const proven = cloudFacts([component()], [supported('r1ReplayableProvable')])
    expect(proven).toMatchObject({ watched: { good: true }, counts: { text: 'Counts messages' }, evidence: { text: 'Can reach Verified', tone: 'good' } })

    const notWatched = cloudFacts([component({ watched: false, deadLetters: { value: null, kind: 'notReported' } })], [supported('r2ReplayableUnproven')])
    expect(notWatched).toMatchObject({ watched: { text: 'Recorded when you look', good: false }, counts: { text: 'No message counts', good: false }, evidence: { text: 'Replays end Unverified' } })
  })

  it('calls a cloud with only unvalidated components not yet validated, and one with nothing covered no replay', () => {
    expect(cloudFacts([component()], [{ class: 'r0Unknown', status: 'beingValidated' }]).evidence.text).toBe('Not yet validated')
    expect(cloudFacts([], []).evidence.text).toBe('No replay offered')
  })
})

import { outcomeFootnote, outcomeWords, recoveryWindows } from '@/next/lib/words'

describe('outcomeWords', () => {
  const at = (iso: string) => `at ${iso}`
  const v = (status: string, over: Partial<{ confidence: 'Exact' | 'Heuristic' | null; watchUntil: string | null }> = {}) => ({ status, confidence: null, watchUntil: null, ...over })

  it('uses the four outcome words, and never "fixed" or "succeeded"', () => {
    expect(outcomeWords(v('verified'), at).word).toBe('Verified')
    expect(outcomeWords(v('verification_required'), at).word).toBe('Unverified')
    expect(outcomeWords(v('returned'), at).word).toBe('Came back')
    expect(outcomeWords(v('watching'), at).word).toBe('Watching')
    for (const status of ['verified', 'verification_required', 'returned', 'not_sent', 'unknown', 'watching']) {
      const o = outcomeWords(v(status), at)
      expect(`${o.word} ${o.observed}`, status).not.toMatch(/fixed|succeed/i)
    }
  })

  it('says only what was observed, and how a return was matched', () => {
    expect(outcomeWords(v('returned', { confidence: 'Exact' }), at).observed).toContain('its recovery ID')
    expect(outcomeWords(v('returned', { confidence: 'Heuristic' }), at).observed).toContain('its contents')
    expect(outcomeWords(v('watching', { watchUntil: '2026-10-19T10:00:00Z' }), at).observed).toBe('Watching for it to fail again until at 2026-10-19T10:00:00Z')
    expect(outcomeWords(v('unknown'), at)).toMatchObject({ word: 'Outcome unknown', tone: 'caution' })
    expect(outcomeWords(v('not_sent'), at).observed).toContain('still in the dead-letter queue')
  })

  it('an unverified replay is amber, not red: it is not a failure', () => {
    expect(outcomeWords(v('verification_required'), at).tone).toBe('caution')
  })
})

describe('outcomeFootnote and the periods', () => {
  it('says what Verified means where it can be reached, and why it cannot where it cannot', () => {
    expect(outcomeFootnote(true)).toContain('did not come back')
    expect(outcomeFootnote(false)).toContain('ends Unverified, never Verified')
  })
  it('offers the four periods the API accepts', () => {
    expect(recoveryWindows.map((w) => w.id)).toEqual(['24h', '7d', '30d', 'all'])
  })
})

import { channelsWords, observerWords } from '@/next/lib/words'

describe('channelsWords', () => {
  it('names every enabled channel, since a channel has no component', () => {
    expect(channelsWords(undefined)).toBe('Not read')
    expect(channelsWords([])).toBe('No channel is set up, so only the bell tells you')
    expect(channelsWords([{ label: '#a', enabled: false }])).toBe('No channel is set up, so only the bell tells you')
    expect(channelsWords([{ label: '#a', enabled: true }, { label: '#b', enabled: true }, { label: '#off', enabled: false }])).toBe('Every enabled channel (2): #a, #b')
  })
})

describe('observerWords', () => {
  const t = (iso: string) => `at ${iso}`
  it('says not set up, live, or set up but not live, never "healthy"', () => {
    expect(observerWords({ enabled: false, live: false, lastConfirmedAt: null }, t)).toBe('Not set up')
    expect(observerWords({ enabled: true, live: true, lastConfirmedAt: 'X' }, t)).toBe('Live, last confirmed at X')
    expect(observerWords({ enabled: true, live: false, lastConfirmedAt: 'X' }, t)).toBe('Set up, but not live: last confirmed at X')
    expect(observerWords({ enabled: true, live: false, lastConfirmedAt: null }, t)).toBe('Set up, but not live: never confirmed yet')
  })
})
