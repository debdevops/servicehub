import { describe, expect, it } from 'vitest'
import { L4, L5, holdReason, mayActAlone, recentlyMixed, signaturesOf, trustOf } from '@/lib/demo/world/derive'
import { buildWorld, capabilities, stories } from '@/lib/demo/world/seed'

const NOW = Date.UTC(2026, 9, 4, 12)

describe('the demo world', () => {
  it('is the same every time: only its timestamps move with the clock', () => {
    const a = buildWorld(NOW)
    const b = buildWorld(NOW)
    expect(b).toEqual(a)
    const later = buildWorld(NOW + 3_600_000)
    expect(later.deadLetters.map((d) => [d.id, d.messageId, d.entityName, d.story])).toEqual(a.deadLetters.map((d) => [d.id, d.messageId, d.entityName, d.story]))
    expect(later.entries.map((e) => [e.id, e.state, e.actor.identity])).toEqual(a.entries.map((e) => [e.id, e.state, e.actor.identity]))
  })

  it('has three clouds, a dev and a test namespace each, and the five failure stories on every cloud', () => {
    const w = buildWorld(NOW)
    expect(w.namespaces.map((n) => `${n.provider}:${n.environment}`)).toEqual(['azure:dev', 'azure:uat', 'aws:dev', 'aws:uat', 'gcp:dev', 'gcp:uat'])
    for (const provider of ['azure', 'aws', 'gcp'] as const) {
      const rows = w.deadLetters.filter((d) => w.namespaces.find((n) => n.id === d.namespaceId)!.provider === provider)
      expect(new Set(rows.map((d) => d.story))).toEqual(new Set(Object.keys(stories)))
      expect(rows.length).toBeGreaterThanOrEqual(150)
    }
    expect(w.entries.length).toBeGreaterThanOrEqual(300)
  })

  it('keeps each cloud’s real capabilities — the demo never flatters a cloud', () => {
    expect(capabilities.azure.canProveDlqAbsence).toBe(true)
    expect(capabilities.aws.canProveDlqAbsence).toBe(false)
    expect(capabilities.gcp.canProveDlqAbsence).toBe(false)
    expect(capabilities.gcp.supportsMessageCounts).toBe(false)
    expect(capabilities.azure.supportsPurge).toBe(false)
    const w = buildWorld(NOW)
    // Nothing on a cloud that cannot prove a fix is ever recorded as verified or as having come back.
    expect(w.entries.filter((e) => e.provider !== 'azure' && (e.state === 'Recovered' || e.state === 'Returned'))).toEqual([])
    // And those clouds record no dead-letter reason.
    expect(w.deadLetters.filter((d) => w.namespaces.find((n) => n.id === d.namespaceId)!.provider !== 'azure' && d.deadLetterReason !== null)).toEqual([])
  })

  it('earns trust only from counted outcomes, at the real thresholds', () => {
    const w = buildWorld(NOW)
    const azure = signaturesOf(w, { provider: 'azure' }, 30, NOW).map((s) => ({ s, t: trustOf(w, s.signatureHash, 'azure') }))
    const levels = azure.map((x) => x.t.level)
    expect(levels).toContain('unattended')
    expect(levels).toContain('standing')
    expect(levels).toContain('approve')
    for (const { t } of azure) {
      if (t.level === 'unattended') expect(t.sample >= L5.sample && t.rate! >= L5.rate).toBe(true)
      if (t.level === 'standing') expect(t.sample >= L4.sample && t.rate! >= L4.rate).toBe(true)
    }
    for (const provider of ['aws', 'gcp'] as const) {
      for (const s of signaturesOf(w, { provider }, 30, NOW)) expect(trustOf(w, s.signatureHash, provider).level).toBe('approve')
    }
  })

  it('uses only invented names', () => {
    const text = JSON.stringify(buildWorld(NOW))
    for (const real of ['servicehub-dev', 'sb-servicehub', '502914', 'ap-south-1', 'debasis']) expect(text.toLowerCase()).not.toContain(real)
  })

  describe('the recent-results guard (RecentResultsGuardGate)', () => {
    const earnedRule = (w: ReturnType<typeof buildWorld>) =>
      w.rules.find((r) => r.enabled && r.signatureHash && trustOf(w, r.signatureHash, r.provider).level !== 'approve')

    it('leaves the seeded rules that earned replaying alone as they were', () => {
      const w = buildWorld(NOW)
      const rule = earnedRule(w)
      expect(rule, 'the seed has an Azure rule that earned replaying alone').toBeDefined()
      expect(recentlyMixed(w, rule!.signatureHash!)).toBe(false)
      expect(mayActAlone(w, rule!)).toBe(true)
    })

    it('hands a failure back to a person once 2 of its latest 10 verified replays did not hold, however good its history', () => {
      const w = buildWorld(NOW)
      const rule = earnedRule(w)!
      const sig = rule.signatureHash!
      const recent = w.entries.filter((e) => e.kind === 'Replay' && e.signatureHash === sig && (e.state === 'Recovered' || e.state === 'Returned' || e.state === 'ExecutionFailed'))
        .sort((a, b) => (b.closedAt ?? b.begunAt).localeCompare(a.closedAt ?? a.begunAt))
      recent[0].state = 'Returned'
      expect(mayActAlone(w, rule), 'one bad result is tolerated').toBe(true)
      recent[1].state = 'ExecutionFailed'
      expect(trustOf(w, sig, rule.provider).level, 'the all-time score still looks fine').not.toBe('approve')
      expect(recentlyMixed(w, sig)).toBe(true)
      expect(mayActAlone(w, rule)).toBe(false)
      expect(holdReason(w, rule).code).toBe('SIGNATURE_RECENT_RESULTS_MIXED')
    })

    it('does not change what a cloud that cannot prove a fix says', () => {
      const w = buildWorld(NOW)
      const aws = w.rules.find((r) => r.provider === 'aws')
      if (aws) expect(holdReason(w, aws).code).toBe('PROVIDER_CANNOT_VERIFY_ABSENCE')
    })
  })
})
