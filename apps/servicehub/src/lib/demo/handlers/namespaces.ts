import type { DeadLetterLook, EntityKind, EntityList, NamespaceStats } from '../../api/namespaces'
import { look } from '../world/actions'
import { activeIn, nsOf } from '../world/derive'
import { people } from '../world/seed'
import { notFound, refuse, type Route } from './http'

const kinds: readonly EntityKind[] = ['queue', 'topic', 'subscription']

export const namespaces: readonly Route[] = [
  ['get', /^\/namespaces$/, (_m, { w }) => w.namespaces],
  ['get', /^\/namespaces\/([^/]+)$/, (m, { w }) => w.namespaces.find((n) => n.id === m[1]) ?? notFound('namespace')],
  ['post', /^\/namespaces$/, () => refuse(409, 'demo_cannot_connect', 'This is a demo — it cannot connect to a real cloud. Leave the demo to connect your own.')],
  ['delete', /^\/namespaces\/([^/]+)$/, () => refuse(409, 'demo_keeps_its_clouds', 'The demo keeps its three clouds connected. Use “Reset demo” to start over.')],
  ['post', /^\/namespaces\/([^/]+)\/test-connection$/, (_m, { now }) => ({ isConnected: true, message: 'Demo — the connection was not really tested.', testedAt: new Date(now).toISOString() })],
  ['get', /^\/namespaces\/([^/]+)\/stats$/, (m, { w, now }): NamespaceStats => {
    const ns = w.namespaces.find((n) => n.id === m[1]) ?? notFound('namespace')
    const list = w.entities[ns.id] ?? []
    const counts = ns.capabilities!.supportsMessageCounts
    return {
      namespaceId: ns.id, entities: kinds.map((kind) => ({ kind, count: list.filter((e) => e.kind === kind).length })),
      activeMessages: counts ? list.reduce((sum, e) => sum + (e.activeMessages ?? 0), 0) : null, deadLetterMessages: counts ? activeIn(w, ns.id).length : null,
      messageCountsSupported: counts, observedAt: new Date(now).toISOString(),
    }
  }],
  ['get', /^\/namespaces\/([^/]+)\/entities$/, (m, { w, params }): EntityList => {
    const ns = w.namespaces.find((n) => n.id === m[1]) ?? notFound('namespace')
    const counts = ns.capabilities!.supportsMessageCounts
    return {
      namespaceId: ns.id,
      entities: (w.entities[ns.id] ?? [])
        .filter((e) => !params.kind || e.kind === params.kind)
        .map((e) => ({ ...e, deadLetterMessages: counts && e.kind !== 'topic' ? activeIn(w, ns.id, e.name).length : null })),
    }
  }],
  ['post', /^\/namespaces\/([^/]+)\/dead-letters\/look$/, (m, { w, now }): DeadLetterLook => {
    const ns = nsOf(w, m[1]) ?? notFound('namespace')
    const seen = look(w, ns.id, people.you, now)
    return { outcome: 'looked', queuesExamined: seen.queues, newMessages: seen.newMessages, resolved: 0, unconfirmed: 0, countsAsDeliveryAttempt: !ns.capabilities!.supportsRepeatablePeek, reason: null, lookedAtUtc: new Date(now).toISOString() }
  }],
]
