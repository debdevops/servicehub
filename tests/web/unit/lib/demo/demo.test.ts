import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fetchAgents, pauseAgent } from '@/lib/api/agents'
import { fetchBulk, previewBulk, startBulk } from '@/lib/api/bulk'
import { api, toProblem } from '@/lib/api/client'
import { fetchDeadLetter, fetchDeadLetters, fetchDeadLetterTrend } from '@/lib/api/deadLetters'
import { fetchFleet } from '@/lib/api/fleet'
import { connectNamespace, fetchEntities, fetchNamespaces, fetchNamespaceStats, lookAtDeadLetters } from '@/lib/api/namespaces'
import { approvePending, declinePending, fetchPendingWork } from '@/lib/api/pendingWork'
import { fetchLedger, fetchLedgerEntry, fetchRecoverySummary, verifyChain } from '@/lib/api/recovery'
import { fetchReplayProposal, fetchReplays, purgeMessage, replayMessage } from '@/lib/api/replay'
import { createRule, fetchRuleMatches, fetchRules, fetchRulesHeld, fetchRuleSources, setRuleEnabled, testRule } from '@/lib/api/rules'
import { fetchEmergencyStop, setEmergencyStop } from '@/lib/api/settings'
import { fetchAuthority, fetchSignatures, fetchSignatureTrust } from '@/lib/api/signatures'
import { enterDemo, isDemo, leaveDemo } from '@/lib/demo/state'
import { tick } from '@/lib/demo/world/clock'
import { DEMO_WATCH_MS } from '@/lib/demo/world/seed'
import { resetWorld } from '@/lib/demo/world/store'

const clouds = ['azure', 'aws', 'gcp'] as const
const START = Date.UTC(2026, 9, 4, 12)

describe('demo mode', () => {
  beforeEach(() => {
    vi.useFakeTimers({ now: START, toFake: ['Date', 'setTimeout', 'clearTimeout', 'setInterval', 'clearInterval'] })
    enterDemo()
    resetWorld(START)
  })
  afterEach(() => {
    leaveDemo()
    vi.useRealTimers()
  })

  const firstActive = async (provider: (typeof clouds)[number], reason?: string) => (await fetchDeadLetters({ provider, status: 'active', reason, page: 1, pageSize: 1 })).items[0]
  const passTheWatch = () => vi.setSystemTime(Date.now() + DEMO_WATCH_MS + 1000)

  it('answers the real client from made-up data, with each cloud’s real capability differences', async () => {
    expect(isDemo()).toBe(true)
    const namespaces = await fetchNamespaces()
    expect(namespaces).toHaveLength(6)
    const byCloud = Object.fromEntries(namespaces.map((n) => [n.provider, n.capabilities!]))
    expect(byCloud.azure.canProveDlqAbsence).toBe(true)
    expect(byCloud.aws.canProveDlqAbsence).toBe(false)
    expect(byCloud.gcp.supportsMessageCounts).toBe(false)
  })

  describe('one truth — every number is counted from the same rows', () => {
    it.each(clouds)('%s: tiles, lists, the fleet and the ledger agree', async (provider) => {
      const namespaces = (await fetchNamespaces()).filter((n) => n.provider === provider)
      const fleet = await fetchFleet('24h')
      let stuck = 0
      for (const ns of namespaces) {
        const list = await fetchDeadLetters({ namespaceId: ns.id, status: 'active', page: 1, pageSize: 1 })
        stuck += list.paging.total
        expect(list.groups.reduce((n, g) => n + g.count, 0)).toBe(list.paging.total)
        expect(fleet.namespaces.find((n) => n.id === ns.id)!.active).toBe(list.paging.total)
        const stats = await fetchNamespaceStats(ns.id)
        if (stats.messageCountsSupported) {
          expect(stats.deadLetterMessages).toBe(list.paging.total)
          const entities = await fetchEntities(ns.id)
          expect(entities.entities.reduce((n, e) => n + (e.deadLetterMessages ?? 0), 0)).toBe(list.paging.total)
        } else {
          expect(stats.deadLetterMessages).toBeNull()
        }
      }
      expect(fleet.clouds.find((c) => c.provider === provider)!.active).toBe(stuck)
      expect((await fetchDeadLetters({ provider, status: 'active', page: 1, pageSize: 1 })).paging.total).toBe(stuck)

      const summary = await fetchRecoverySummary({ provider, window: 'all' })
      const ledger = await fetchLedger({ provider, window: 'all', page: 1, pageSize: 1 })
      expect(summary.total).toBe(ledger.total)
      expect(summary.states.reduce((n, s) => n + s.count, 0)).toBe(summary.total)
      expect((await fetchReplays({ provider, window: 'all', page: 1, pageSize: 1 })).total).toBe(summary.replaysAccepted)

      const rules = await fetchRules(provider)
      for (const rule of rules) {
        expect((await fetchReplays({ provider, ruleId: rule.id, window: 'all', page: 1, pageSize: 1 })).total).toBe(rule.replayed)
        expect(rule.stayedFixed).toBeLessThanOrEqual(rule.verifiedOutcomes)
      }
      const pending = await fetchPendingWork({ provider })
      expect(pending.items).toHaveLength(Math.min(pending.total, 100))
      expect(pending.byProvider.reduce((n, p) => n + p.count, 0)).toBe(pending.total)
      expect((await fetchRulesHeld(provider)).distinct).toBe(pending.total - pending.items.filter((i) => i.kind === 'rule').length)

      const signatures = await fetchSignatures({ provider, days: 30, tab: 'all', sort: 'messages', page: 1, pageSize: 50 })
      expect(signatures.items.reduce((n, s) => n + s.activeNow, 0)).toBe(stuck)
      const authority = await fetchAuthority({ provider, days: 30 })
      expect(authority.unattended + authority.standing + authority.approve).toBe(signatures.all)
      expect(authority.held.reduce((n, h) => n + h.count, 0)).toBe(authority.approve)
    })

    it('the trend adds up to what the list shows', async () => {
      const trend = await fetchDeadLetterTrend('azure', 30)
      const all = await fetchDeadLetters({ provider: 'azure', status: 'all', page: 1, pageSize: 1 })
      expect(trend.series.reduce((n, d) => n + d.new, 0)).toBe(all.paging.total)
    })

    it('the ledger’s chain counts every event of every entry', async () => {
      const ledger = await fetchLedger({ window: 'all', page: 1, pageSize: 500 })
      let events = 0
      for (const e of ledger.items.slice(0, 25)) events += (await fetchLedgerEntry(e.id)).events.length
      expect(events).toBeGreaterThan(25)
      expect((await verifyChain()).isValid).toBe(true)
    })
  })

  describe('each cloud stays honest', () => {
    it('never shows a verified replay where the cloud cannot prove one', async () => {
      for (const provider of ['aws', 'gcp'] as const) {
        const replays = await fetchReplays({ provider, window: 'all', page: 1, pageSize: 500 })
        expect(replays.items.length).toBeGreaterThan(0)
        expect(replays.items.every((r) => r.verification.status === 'verification_required')).toBe(true)
        const signatures = await fetchSignatures({ provider, days: 30, tab: 'all', sort: 'messages', page: 1, pageSize: 50 })
        for (const s of signatures.items) expect((await fetchSignatureTrust(s.signatureHash, provider)).cloudCanConfirm).toBe(false)
      }
      const azure = await fetchReplays({ provider: 'azure', window: 'all', page: 1, pageSize: 500 })
      expect(azure.items.some((r) => r.verification.status === 'verified')).toBe(true)
      expect(azure.items.some((r) => r.verification.status === 'returned')).toBe(true)
    })

    it('asks a person on AWS and Google, and lets rules act alone on Azure only where trust was earned', async () => {
      expect((await fetchPendingWork({ provider: 'aws' })).items.some((i) => i.kind === 'approval' && i.reasonCode === 'PROVIDER_CANNOT_VERIFY_ABSENCE')).toBe(true)
      const azure = await fetchPendingWork({ provider: 'azure' })
      expect(azure.items.filter((i) => i.kind === 'approval')).toEqual([])
      expect(azure.items.some((i) => i.kind === 'rule')).toBe(true)
      expect((await fetchRules('azure')).some((r) => !r.enabled && r.disabledReason === 'CircuitBreaker')).toBe(true)
    })
  })

  describe('actions work on the made-up world', () => {
    it('Azure: a replay whose cause is fixed is watched, then verified', async () => {
      const message = await firstActive('azure', 'UnauthorizedAccess')
      const proposal = await fetchReplayProposal(message.id)
      expect(proposal.canExecute && proposal.canConfirm).toBe(true)
      const outcome = await replayMessage(message.id)
      expect(outcome.result).toBe('accepted')
      expect(outcome.message).toMatch(/Demo — nothing was sent/)
      expect((await fetchDeadLetter(message.id)).item.status).not.toBe('active')
      expect((await fetchReplays({ dlqMessageId: message.id, page: 1, pageSize: 5 })).items[0].verification.status).toBe('watching')
      passTheWatch()
      expect((await fetchReplays({ dlqMessageId: message.id, page: 1, pageSize: 5 })).items[0].verification.status).toBe('verified')
      await expect(replayMessage(message.id)).rejects.toMatchObject({ response: { status: 409 } })
    })

    it('Azure: a replay whose cause is not fixed comes back, and the message is stuck again', async () => {
      const message = await firstActive('azure', 'ValidationFailed')
      await replayMessage(message.id)
      passTheWatch()
      expect((await fetchReplays({ dlqMessageId: message.id, page: 1, pageSize: 5 })).items[0].verification.status).toBe('returned')
      expect((await fetchDeadLetter(message.id)).item.status).toBe('active')
    })

    it('AWS: a replay is accepted and ends “verification required”, never verified', async () => {
      const message = await firstActive('aws')
      expect((await fetchReplayProposal(message.id)).canConfirm).toBe(false)
      await replayMessage(message.id)
      passTheWatch()
      expect((await fetchReplays({ dlqMessageId: message.id, page: 1, pageSize: 5 })).items[0].verification.status).toBe('verification_required')
    })

    it('purge follows the cloud: refused on Azure, recorded on AWS', async () => {
      await expect(purgeMessage((await firstActive('azure')).id, 'test')).rejects.toMatchObject({ response: { status: 409 } })
      const message = await firstActive('aws')
      await purgeMessage(message.id, 'poison')
      expect((await fetchDeadLetter(message.id)).item.resolutionCause).toBe('purgedByServiceHub')
    })

    it('approve replays the held message; decline stops the Agent asking about it', async () => {
      const before = await fetchPendingWork({ provider: 'aws' })
      const [first, second] = before.items.filter((i) => i.kind === 'approval')
      expect((await approvePending(first.entryId!)).result).toBe('accepted')
      await expect(declinePending(second.entryId!, '')).rejects.toBeTruthy()
      await declinePending(second.entryId!, 'Known bad batch')
      expect((await fetchPendingWork({ provider: 'aws' })).total).toBe(before.total - 2)
    })

    it('bulk replay runs to the end, one message at a time', async () => {
      const ids = (await fetchDeadLetters({ provider: 'azure', status: 'active', reason: 'MaxDeliveryCountExceeded', page: 1, pageSize: 12 })).items.map((d) => d.id)
      const preview = await previewBulk(ids)
      expect(preview.willReplay).toBe(12)
      const started = await startBulk(preview.previewId, false)
      expect(started.status).toBe('running')
      vi.setSystemTime(Date.now() + 10_000)
      const done = await fetchBulk(started.id)
      expect(done).toMatchObject({ status: 'completed', sent: 12, remaining: 0 })
      expect((await fetchReplays({ provider: 'azure', window: '24h', ending: 'watching', page: 1, pageSize: 50 })).total).toBeGreaterThanOrEqual(12)
    })

    it('a new rule on AWS matches what is stuck and holds it for a person', async () => {
      const [source] = await fetchRuleSources('aws')
      expect(source.messages).toBeGreaterThan(0)
      const test = await testRule({ provider: 'aws', signatureHash: source.signatureHash })
      expect(test).toMatchObject({ stillWaiting: source.messages, wouldRun: 0, heldBack: source.messages })
      const rule = await createRule({ provider: 'aws', name: 'Timeouts', signatureHash: source.signatureHash, maxPerHour: 30, waitSeconds: 60, backOff: true })
      expect(await fetchRuleMatches(rule.id)).toHaveLength(source.messages)
      expect(rule.askedCount).toBe(source.messages)
      expect((await setRuleEnabled(rule.id, false)).disabledReason).toBe('Person')
    })

    it('the clock: Auto Replay works through what a rule has earned, and the failure climbs to L5', async () => {
      const hash = (await fetchRules('azure')).find((r) => r.enabled && r.name.startsWith('Expired token'))!.signatureHash!
      expect((await fetchSignatureTrust(hash, 'azure')).level).toBe('standing')
      const waiting = (await fetchRules('azure')).find((r) => r.signatureHash === hash)!
      tick(Date.now())
      expect((await fetchReplays({ provider: 'azure', ruleId: waiting.id, ending: 'watching', page: 1, pageSize: 5 })).total).toBe(1)
      passTheWatch()
      expect((await fetchSignatureTrust(hash, 'azure')).level).toBe('unattended')
    })

    it('pausing Auto Replay or the emergency stop stops it acting; a person still can', async () => {
      await pauseAgent('auto-replay')
      expect((await fetchAgents()).find((a) => a.id === 'auto-replay')).toMatchObject({ isPaused: true, health: 'paused' })
      tick(Date.now())
      expect((await fetchReplays({ provider: 'azure', by: 'autonomous', ending: 'watching', page: 1, pageSize: 5 })).total).toBe(0)
      await expect(setEmergencyStop({ active: true, reason: 'test' })).rejects.toBeTruthy()
      expect((await setEmergencyStop({ active: true, reason: 'test', confirm: 'STOP' })).active).toBe(true)
      expect((await fetchEmergencyStop()).active).toBe(true)
      expect((await replayMessage((await firstActive('azure')).id)).result).toBe('accepted')
    })

    it('Look now shows what arrived on a cloud ServiceHub does not watch', async () => {
      const ns = (await fetchNamespaces()).find((n) => n.provider === 'aws' && n.environment === 'dev')!
      const before = (await fetchDeadLetters({ namespaceId: ns.id, status: 'active', page: 1, pageSize: 1 })).paging.total
      const look = await lookAtDeadLetters(ns.id)
      expect(look).toMatchObject({ outcome: 'looked', countsAsDeliveryAttempt: true })
      expect(look.newMessages).toBeGreaterThan(0)
      expect((await fetchDeadLetters({ namespaceId: ns.id, status: 'active', page: 1, pageSize: 1 })).paging.total).toBe(before + look.newMessages)
      expect((await lookAtDeadLetters(ns.id)).newMessages).toBe(0)
    })
  })

  it('refuses, in words, the few things a demo cannot do', async () => {
    const error = await connectNamespace({ name: 'x', provider: 'azure', authType: 'connectionString' }).catch((e: unknown) => e)
    expect(toProblem(error).message).toMatch(/cannot connect to a real cloud/)
    await expect(api.post('/admin/backup')).rejects.toBeTruthy()
  })
})
