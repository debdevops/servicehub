import type { CloudProvider } from '../../api/namespaces'
import type { PendingWorkItem, PendingWorkPage } from '../../api/pendingWork'
import { replay } from '../world/actions'
import { pendingOf, scopeOf } from '../world/derive'
import type { DemoDeadLetter, World } from '../world/model'
import { people } from '../world/seed'
import { emit, save } from '../world/store'
import { notFound, refuse, type Route } from './http'

const held = (w: World, entryId: string): DemoDeadLetter => {
  const row = w.deadLetters.find((d) => `demo-held-${d.id}` === entryId) ?? notFound('request')
  if (row.status !== 'active') refuse(409, 'NOT_ACTIVE', 'This message is no longer in the dead-letter queue, so there is nothing left to decide.')
  return row
}

const PAGE = 100

/** The first page, dealt out one namespace at a time — a busy cloud must not push another's requests off the page (as the real API does). */
function firstPage(items: readonly PendingWorkItem[]): PendingWorkItem[] {
  const lanes = new Map<string, PendingWorkItem[]>()
  for (const item of items) {
    const key = `${item.kind === 'approval' ? 'a' : 'r'}:${item.namespaceId}`
    const lane = lanes.get(key)
    if (lane) lane.push(item)
    else lanes.set(key, [item])
  }
  const page: PendingWorkItem[] = []
  for (let i = 0; page.length < PAGE && page.length < items.length; i++) {
    for (const lane of lanes.values()) if (lane[i] && page.length < PAGE) page.push(lane[i])
  }
  return page
}

export const pendingWork: readonly Route[] = [
  ['get', /^\/pending-work$/, (_m, { w, params }): PendingWorkPage => {
    const items = pendingOf(w, { ...scopeOf(params), reason: typeof params.reason === 'string' ? params.reason : undefined })
    const byProvider = new Map<CloudProvider, number>()
    items.forEach((i) => byProvider.set(i.provider!, (byProvider.get(i.provider!) ?? 0) + 1))
    return { items: firstPage(items), total: items.length, byProvider: [...byProvider].map(([provider, count]) => ({ provider, count })), agents: items.filter((i) => i.kind === 'agent').length }
  }],
  ['post', /^\/pending-work\/([^/]+)\/approve$/, (m, { w, now }) => replay(w, held(w, m[1]), people.you, now)],
  // The demo never stops mid-attempt, so it never holds an attempt with no recorded answer: there is nothing to settle.
  ['post', /^\/pending-work\/([^/]+)\/resolve$/, () => refuse(404, 'NOT_FOUND', 'Nothing unresolved is waiting in the demo — a simulated replay always records its answer.')],
  ['post', /^\/pending-work\/([^/]+)\/decline$/, (m, { w, body }) => {
    const row = held(w, m[1])
    if (!String(body.reason ?? '').trim()) refuse(400, 'VALIDATION_FAILED', 'Say why, so the next person knows.')
    w.declined.push(row.id)
    save()
    emit('EscalationResolved', 'Escalation', row.namespaceId)
    return null
  }],
]
