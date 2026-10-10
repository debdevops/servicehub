import type { BulkHeldBack, BulkPreview, BulkProgress } from '../../api/bulk'
import { audit, purge, replay } from '../world/actions'
import { nsOf, proves, without } from '../world/derive'
import type { DemoBulk, World } from '../world/model'
import { people } from '../world/seed'
import { emit, save } from '../world/store'
import { notFound, refuse, type Route } from './http'

const PER_SECOND = 5
const ATTEMPT_CAP = 3

function preview(w: World, body: Record<string, unknown>): BulkPreview {
  const kind = body.kind === 'purge' ? 'purge' : 'replay'
  const chosen = ((body.dlqMessageIds as number[] | undefined) ?? []).map((id) => w.deadLetters.find((d) => d.id === id)).filter((d) => d !== undefined)
  const heldBack: BulkHeldBack[] = []
  const go: number[] = []
  for (const d of chosen) {
    const hold = (reasonCode: string, remedy: string) => heldBack.push({ dlqMessageId: d.id, entityName: d.entityName, deadLetterReason: d.deadLetterReason, reasonCode, remedy })
    if (d.status !== 'active') hold('NOT_ACTIVE', 'It has already left the dead-letter queue.')
    else if (kind === 'purge' && !nsOf(w, d.namespaceId).capabilities!.supportsPurge) hold('PURGE_UNSUPPORTED', 'This cloud cannot delete one chosen message.')
    else if (kind === 'replay' && w.entries.filter((e) => e.dlqMessageId === d.id && e.kind === 'Replay').length >= ATTEMPT_CAP) hold('RECURRENCE_CAP_EXCEEDED', 'It has been replayed three times and came back each time. Look at why before trying again.')
    else go.push(d.id)
  }
  const held = new Set(heldBack.map((h) => h.dlqMessageId))
  const reasons = [...new Set(chosen.map((d) => d.deadLetterReason ?? 'No reason recorded'))]
  const previewId = `demo-preview-${w.next.bulk++}`
  w.previews[previewId] = { ids: go, kind, reason: typeof body.reason === 'string' ? body.reason : null }
  save()
  return {
    previewId, selected: chosen.length, willReplay: go.length, heldBackCount: heldBack.length,
    groups: reasons.map((reason) => {
      const group = chosen.filter((d) => (d.deadLetterReason ?? 'No reason recorded') === reason)
      return { reason, selected: group.length, willReplay: group.filter((d) => !held.has(d.id)).length, heldBack: group.filter((d) => held.has(d.id)).length }
    }),
    heldBack, perSecond: PER_SECOND, stopAfterConsecutiveFailures: 5, expiresInMinutes: 15, canProveDlqAbsence: chosen.every((d) => proves(nsOf(w, d.namespaceId).provider)), kind,
  }
}

/** Moves a running job on by however much time has passed, replaying or purging each message as its turn comes. */
function advance(w: World, job: DemoBulk, now: number): DemoBulk {
  if (job.status !== 'running' || !job.startedAt) return job
  const due = Math.min(job.ids.length, Math.floor(((now - Date.parse(job.startedAt)) / 1000) * PER_SECOND) + 1)
  let sent = job.sent
  for (let i = job.sent; i < due; i++) {
    const row = w.deadLetters.find((d) => d.id === job.ids[i])
    if (row?.status === 'active') {
      if (job.kind === 'purge') purge(w, row, people.you, now)
      else replay(w, row, people.you, now)
    }
    sent++
  }
  const done = sent >= job.ids.length
  const next: DemoBulk = { ...job, sent, remaining: job.ids.length - sent, status: done ? 'completed' : 'running', endedAt: done ? new Date(now).toISOString() : null }
  w.bulk[job.id] = next
  if (done) emit('BulkOperationCompleted', 'Bulk', null)
  save()
  return next
}

/** The job as the API returns it — without the demo's own list of which messages it holds. */
const progress = (job: DemoBulk): BulkProgress => without(job, 'ids', 'purgeReason')

export const bulk: readonly Route[] = [
  ['post', /^\/bulk-operations\/preview$/, (_m, { w, body }) => preview(w, body)],
  ['post', /^\/bulk-operations$/, (_m, { w, body, now }) => {
    const previewId = String(body.previewId ?? '')
    const previewed = w.previews[previewId] ?? refuse(409, 'PREVIEW_EXPIRED', 'That preview is no longer there. Preview again, then start.')
    if (previewed.kind === 'purge' && body.confirm !== 'PURGE') refuse(400, 'VALIDATION_FAILED', 'Type PURGE to confirm.')
    const ids = body.sampleOnly === true ? previewed.ids.slice(0, 1) : previewed.ids
    const job: DemoBulk = {
      id: `demo-bulk-${w.next.bulk++}`, status: 'running', selected: previewed.ids.length, willReplay: ids.length, sent: 0, failed: 0, unknown: 0, remaining: ids.length, heldBack: 0, sampleOnly: body.sampleOnly === true,
      endedReason: null, previewedAt: new Date(now).toISOString(), startedAt: new Date(now).toISOString(), endedAt: null, kind: previewed.kind, problems: [], ids, purgeReason: previewed.reason,
    }
    delete w.previews[previewId]
    w.bulk[job.id] = job
    audit(w, people.you, 'Replay.Bulk', null, `${ids.length} message${ids.length === 1 ? '' : 's'}`, now)
    return progress(advance(w, job, now))
  }],
  ['get', /^\/bulk-operations\/([^/]+)$/, (m, { w, now }) => progress(advance(w, w.bulk[m[1]] ?? notFound('run'), now))],
  ['post', /^\/bulk-operations\/([^/]+)\/cancel$/, (m, { w, now }) => {
    const job = advance(w, w.bulk[m[1]] ?? notFound('run'), now)
    if (job.status !== 'running') return progress(job)
    const stopped: DemoBulk = { ...job, status: 'cancelled', endedReason: 'Stopped by a person.', endedAt: new Date(now).toISOString() }
    w.bulk[job.id] = stopped
    save()
    return progress(stopped)
  }],
]
