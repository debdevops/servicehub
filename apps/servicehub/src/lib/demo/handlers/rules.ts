import type { CloudProvider } from '../../api/namespaces'
import type { RuleSource, RuleTest } from '../../api/rules'
import { audit, ruleById } from '../world/actions'
import { DAY, holdReason, lowerProvider, matchesOf, mayActAlone, nsOf, pendingOf, signaturesOf, toRule } from '../world/derive'
import type { DemoRule, World } from '../world/model'
import { people } from '../world/seed'
import { emit, save } from '../world/store'
import { notFound, refuse, type Route } from './http'

const providerOf = (v: unknown): CloudProvider => lowerProvider(v) ?? refuse(400, 'VALIDATION_FAILED', 'Say which cloud the rule is for.')
const find = (w: World, id: string): DemoRule => ruleById(w, Number(id)) ?? notFound('rule')

/** Kinds of failure that are stuck right now and have no rule yet — what a new rule could be made from. */
function sources(w: World, provider: CloudProvider, now: number): RuleSource[] {
  const taken = new Set(w.rules.filter((r) => r.provider === provider).map((r) => r.signatureHash))
  return signaturesOf(w, { provider }, 30, now)
    .filter((s) => s.activeNow > 0 && !taken.has(s.signatureHash))
    .sort((a, b) => b.activeNow - a.activeNow)
    .map((s) => ({ signatureHash: s.signatureHash, reason: s.reason, entityName: s.entities[0], messages: s.activeNow, exampleError: s.exampleError }))
}

function create(w: World, provider: CloudProvider, input: Record<string, unknown>, now: number): DemoRule {
  const namespace = w.namespaces.find((n) => n.provider === provider)!
  const rule: DemoRule = {
    id: w.next.rule++, name: String(input.name ?? 'New rule'), provider, namespaceId: namespace.id,
    reason: typeof input.reason === 'string' ? input.reason : null, entityName: typeof input.entityName === 'string' ? input.entityName : null, signatureHash: typeof input.signatureHash === 'string' ? input.signatureHash : null,
    maxPerHour: Number(input.maxPerHour ?? 60), waitSeconds: Number(input.waitSeconds ?? 60), backOff: input.backOff !== false, enabled: true, disabledReason: null, disabledDetail: null, updatedAt: new Date(now).toISOString(),
  }
  w.rules.push(rule)
  return rule
}

export const rules: readonly Route[] = [
  ['get', /^\/rules$/, (_m, { w, params }) => w.rules.filter((r) => r.provider === providerOf(params.provider)).map((r) => toRule(w, r))],
  ['get', /^\/rules\/held$/, (_m, { w, params }) => ({ distinct: pendingOf(w, { provider: providerOf(params.provider) }).filter((i) => i.kind === 'approval').length, isLowerBound: false })],
  ['get', /^\/rules\/sources$/, (_m, { w, params, now }) => sources(w, providerOf(params.provider), now)],
  ['get', /^\/rules\/(\d+)\/matches$/, (m, { w, params }) => matchesOf(w, find(w, m[1])).slice(0, Number(params.limit ?? 500)).map((d) => d.id)],
  ['post', /^\/rules$/, (_m, { w, body, now }) => {
    const rule = create(w, providerOf(body.provider), body, now)
    audit(w, people.you, 'Rule.Create', rule.namespaceId, rule.name, now)
    save()
    emit('RuleChanged', 'Rule', rule.namespaceId)
    return toRule(w, rule)
  }],
  ['post', /^\/rules\/generate$/, (_m, { w, body, now }) => {
    const provider = providerOf(body.provider)
    const made = sources(w, provider, now).slice(0, Number(body.max ?? 5)).map((s) => create(w, provider, { name: `${s.reason === 'Unknown' ? 'Failures' : s.reason} — ${s.entityName}`, reason: s.reason === 'Unknown' ? undefined : s.reason, entityName: s.entityName, signatureHash: s.signatureHash }, now))
    if (made.length) audit(w, people.you, 'Rule.Generate', made[0].namespaceId, `${made.length} rule${made.length === 1 ? '' : 's'}`, now)
    save()
    emit('RuleChanged', 'Rule', null)
    return made.map((r) => toRule(w, r))
  }],
  ['post', /^\/rules\/test$/, (_m, { w, body, now }): RuleTest => {
    const provider = providerOf(body.provider)
    const days = Number(body.days ?? 7)
    const probe: DemoRule = { id: -1, name: '', provider, namespaceId: '', reason: typeof body.reason === 'string' ? body.reason : null, entityName: typeof body.entityName === 'string' ? body.entityName : null, signatureHash: typeof body.signatureHash === 'string' ? body.signatureHash : null, maxPerHour: 0, waitSeconds: 0, backOff: false, enabled: true, disabledReason: null, disabledDetail: null, updatedAt: null }
    const waiting = matchesOf(w, probe)
    const matched = w.deadLetters.filter((d) => nsOf(w, d.namespaceId).provider === provider && now - Date.parse(d.detectedAtUtc) <= days * DAY
      && (probe.signatureHash ? d.signatureHash === probe.signatureHash : (!probe.entityName || d.entityName === probe.entityName) && (!probe.reason || d.deadLetterReason === probe.reason))).length
    const alone = mayActAlone(w, probe)
    const { code, reason } = holdReason(w, probe)
    return { days, matched, stillWaiting: waiting.length, wouldRun: alone ? waiting.length : 0, heldBack: alone ? 0 : waiting.length, holds: alone || waiting.length === 0 ? [] : [{ reasonCode: code, remedy: reason, count: waiting.length }] }
  }],
  ['post', /^\/rules\/(\d+)\/enabled$/, (m, { w, body, now }) => {
    const rule = find(w, m[1])
    rule.enabled = body.enabled === true
    rule.disabledReason = rule.enabled ? null : 'Person'
    rule.disabledDetail = rule.enabled ? null : `Turned off by ${people.you.label}.`
    rule.updatedAt = new Date(now).toISOString()
    audit(w, people.you, 'Rule.Toggle', rule.namespaceId, rule.name, now)
    save()
    emit('RuleChanged', 'Rule', rule.namespaceId)
    return toRule(w, rule)
  }],
  ['put', /^\/rules\/(\d+)$/, (m, { w, body, now }) => {
    const rule = find(w, m[1])
    if (typeof body.name === 'string') rule.name = body.name
    if (body.maxPerHour !== undefined) rule.maxPerHour = Number(body.maxPerHour)
    if (body.waitSeconds !== undefined) rule.waitSeconds = Number(body.waitSeconds)
    if (body.backOff !== undefined) rule.backOff = body.backOff === true
    rule.updatedAt = new Date(now).toISOString()
    audit(w, people.you, 'Rule.Update', rule.namespaceId, rule.name, now)
    save()
    return toRule(w, rule)
  }],
  ['delete', /^\/rules\/(\d+)$/, (m, { w, now }) => {
    const rule = find(w, m[1])
    w.rules = w.rules.filter((r) => r.id !== rule.id)
    audit(w, people.you, 'Rule.Delete', rule.namespaceId, rule.name, now)
    save()
    emit('RuleChanged', 'Rule', rule.namespaceId)
    return null
  }],
]
