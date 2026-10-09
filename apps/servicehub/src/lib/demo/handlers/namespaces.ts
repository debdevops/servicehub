import type { DeadLetterLook, DlqObserver, EntityKind, EntityList, NamespaceStats } from '../../api/namespaces'
import { look } from '../world/actions'
import { activeIn, nsOf } from '../world/derive'
import { people } from '../world/seed'
import { notFound, refuse, type Route } from './http'

const kinds: readonly EntityKind[] = ['queue', 'topic', 'subscription']

/** The demo clouds that cannot confirm a fix by themselves say so, and the controls refuse: nothing is set up in a demo (R4/R5 — only Azure shows verified). */
export const namespaces: readonly Route[] = [
  ['get', /^\/namespaces\/([^/]+)\/dlq-observer$/, (m, { w }): DlqObserver => {
    const ns = w.namespaces.find((n) => n.id === m[1]) ?? notFound('namespace')
    const needed = !ns.capabilities!.canProveDlqAbsence
    return {
      needed, enabled: false, live: false, observerReference: null, dlqEntityName: null, stalenessBoundMinutes: 30, lastCanarySentAt: null, lastConfirmedAt: null,
      status: needed ? 'No observer is set up, so a replay here can be sent back but not confirmed as fixed.' : 'This cloud can confirm a replay stayed fixed on its own — no observer needed.',
      needsReference: false, referenceHint: null,
    }
  }],
  ['put', /^\/namespaces\/([^/]+)\/dlq-observer$/, () => refuse(409, 'demo_cannot_set_up', 'This is a demo — nothing is connected to a real cloud, so there is nothing to switch on.')],
  ['post', /^\/namespaces\/([^/]+)\/dlq-observer\/check$/, () => refuse(409, 'demo_cannot_set_up', 'This is a demo — nothing is connected to a real cloud, so there is nothing to check.')],
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
