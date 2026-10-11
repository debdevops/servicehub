import type { DeadLetterDetail, DeadLetterPage, DeadLetterTrend } from '../../api/deadLetters'
import type { ReplayProposal } from '../../api/replay'
import { purge, replay } from '../world/actions'
import { DAY, inScope, nsOf, proves, scopeOf, toDeadLetter, withinWindow } from '../world/derive'
import type { DemoDeadLetter, World } from '../world/model'
import { REAL_WATCH_HOURS, bodyOf, people, propertiesOf } from '../world/seed'
import { notFound, refuse, type Route } from './http'

/** How many times one message may be replayed before a person has to look at why. */
const ATTEMPT_CAP = 3

const find = (w: World, id: string): DemoDeadLetter => w.deadLetters.find((d) => d.id === Number(id)) ?? notFound('message')
const attempts = (w: World, d: DemoDeadLetter) => w.entries.filter((e) => e.dlqMessageId === d.id && e.kind === 'Replay').length

function list(w: World, p: Record<string, unknown>, now: number): DeadLetterPage {
  const scope = scopeOf(p)
  const status = String(p.status ?? 'active')
  let rows = w.deadLetters.filter((d) => inScope(w, d.namespaceId, scope) && (status === 'all' || (status === 'resolved' ? d.status !== 'active' : d.status === 'active')) && withinWindow(d.detectedAtUtc, p.range, now))
  if (p.entity) rows = rows.filter((d) => d.entityName === p.entity)
  if (p.q) {
    const q = String(p.q).toLowerCase()
    rows = rows.filter((d) => `${d.messageId} ${d.entityName} ${d.deadLetterReason ?? ''} ${d.deadLetterErrorDescription ?? ''}`.toLowerCase().includes(q))
  }
  const inView = rows
  if (p.noReason) rows = rows.filter((d) => !d.deadLetterReason)
  else if (p.reason) rows = rows.filter((d) => d.deadLetterReason === p.reason)
  rows = [...rows].sort((a, b) => b.detectedAtUtc.localeCompare(a.detectedAtUtc))
  const number = Math.max(1, Number(p.page ?? 1))
  const size = Math.max(1, Number(p.pageSize ?? 25))
  const counts = new Map<string | null, number>()
  inView.forEach((d) => counts.set(d.deadLetterReason, (counts.get(d.deadLetterReason) ?? 0) + 1))
  return {
    items: rows.slice((number - 1) * size, number * size).map((d) => toDeadLetter(w, d)), paging: { total: rows.length, page: number, pageSize: size },
    groups: [...counts].map(([reason, count]) => ({ reason, count })).sort((a, b) => b.count - a.count), otherReasons: null, entities: [...new Set(inView.map((d) => d.entityName))].sort(),
  }
}

function trend(w: World, p: Record<string, unknown>, now: number): DeadLetterTrend {
  const days = Math.max(1, Number(p.days ?? 7))
  const scope = scopeOf(p)
  const rows = w.deadLetters.filter((d) => inScope(w, d.namespaceId, scope))
  const start = now - days * DAY
  const dayOf = (iso: string) => Math.floor((Date.parse(iso) - start) / DAY)
  return {
    days,
    series: Array.from({ length: days }, (_, i) => ({
      date: new Date(start + (i + 1) * DAY).toISOString().slice(0, 10),
      new: rows.filter((d) => dayOf(d.detectedAtUtc) === i).length,
      resolved: rows.filter((d) => d.resolvedAt && dayOf(d.resolvedAt) === i).length,
    })),
  }
}

function proposal(w: World, d: DemoDeadLetter): ReplayProposal {
  const ns = nsOf(w, d.namespaceId)
  const canConfirm = proves(ns.provider)
  const prior = attempts(w, d)
  const active = d.status === 'active'
  const capped = prior >= ATTEMPT_CAP
  return {
    dlqMessageId: d.id, messageId: d.messageId, sourceEntity: d.entityName, targetEntity: d.topicName ?? d.entityName, namespaceName: ns.displayName ?? ns.name, provider: ns.provider, environment: ns.environment,
    stampsRecoveryMarker: true, priorAttempts: prior, attemptCap: ATTEMPT_CAP, othersLikeIt: w.deadLetters.filter((x) => x.signatureHash === d.signatureHash && x.status === 'active' && x.id !== d.id).length,
    observationWindowHours: REAL_WATCH_HOURS, canConfirm, verdict: capped ? 'Escalate' : 'Allow', reasonCode: capped ? 'RECURRENCE_CAP_EXCEEDED' : null, approvable: false,
    canExecute: active && !capped, blockedCode: !active ? 'NOT_ACTIVE' : capped ? 'RECURRENCE_CAP_EXCEEDED' : null,
    checks: [
      { id: 'status', label: 'Still in the dead-letter queue', state: active ? 'passed' : 'blocked', detail: active ? null : 'It has already been replayed or removed.' },
      { id: 'environment', label: 'Not a production namespace', state: 'passed', detail: ns.environment },
      { id: 'frequency', label: 'Not replayed too often', state: capped ? 'blocked' : 'passed', detail: `${prior} of ${ATTEMPT_CAP} earlier attempts` },
      canConfirm
        ? { id: 'verification', label: 'The cloud can confirm whether it stayed fixed', state: 'passed', detail: null }
        : { id: 'verification', label: 'The cloud cannot prove it stayed fixed', state: 'warning', detail: 'The result will read “verification required”, never “verified”.' },
    ],
  }
}

export const deadLetters: readonly Route[] = [
  ['get', /^\/dead-letters$/, (_m, { w, params, now }) => list(w, params, now)],
  ['get', /^\/dead-letters\/trend$/, (_m, { w, params, now }) => trend(w, params, now)],
  ['get', /^\/dead-letters\/(\d+)$/, (m, { w }): DeadLetterDetail => {
    const d = find(w, m[1])
    return {
      item: toDeadLetter(w, d), bodyPreview: bodyOf(d, nsOf(w, d.namespaceId).provider), bodyIsPreview: false, contentType: d.story === 'poison' ? 'text/html' : 'application/json', correlationId: d.correlationId, sessionId: null,
      applicationPropertiesJson: JSON.stringify(propertiesOf(d)), resolvedAt: d.resolvedAt ?? null, othersLikeIt: w.deadLetters.filter((x) => x.signatureHash === d.signatureHash && x.status === 'active' && x.id !== d.id).length,
    }
  }],
  ['get', /^\/dead-letters\/(\d+)\/replay-proposal$/, (m, { w }) => proposal(w, find(w, m[1]))],
  ['post', /^\/dead-letters\/(\d+)\/replay$/, (m, { w, now }) => {
    const d = find(w, m[1])
    if (d.status !== 'active') refuse(409, 'NOT_ACTIVE', 'This message is no longer in the dead-letter queue, so there is nothing to replay.')
    if (attempts(w, d) >= ATTEMPT_CAP) refuse(409, 'RECURRENCE_CAP_EXCEEDED', `This message has already been replayed ${ATTEMPT_CAP} times and came back each time.`)
    return replay(w, d, people.you, now)
  }],
  ['post', /^\/dead-letters\/(\d+)\/purge$/, (m, { w, now }) => {
    const d = find(w, m[1])
    if (!nsOf(w, d.namespaceId).capabilities!.supportsPurge) refuse(409, 'PURGE_UNSUPPORTED', 'This cloud has no way to delete one chosen message, so purge is not offered here.')
    if (d.status !== 'active') refuse(409, 'NOT_ACTIVE', 'This message is no longer in the dead-letter queue.')
    return purge(w, d, people.you, now)
  }],
]
