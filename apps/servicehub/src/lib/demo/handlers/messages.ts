import type { Message, PeekPage, ScheduledPage } from '../../api/messages'
import { audit } from '../world/actions'
import { activeIn, nsOf } from '../world/derive'
import type { DemoDeadLetter, World } from '../world/model'
import { bodyOf, people, propertiesOf } from '../world/seed'
import { save } from '../world/store'
import { DEMO_NOTE, notFound, refuse, type Route } from './http'

/** The API names a subscription by its topic and its own name; the demo stores it as `topic/subscriptions/name`. */
const entityName = (p: Record<string, unknown>) => {
  const entity = String(p.entity ?? '')
  const subscription = p.subscription ? String(p.subscription) : null
  return subscription && !entity.includes('/subscriptions/') ? `${entity}/subscriptions/${subscription.replace(/^.*\/subscriptions\//, '')}` : entity
}

const fromDeadLetter = (w: World, d: DemoDeadLetter): Message => ({
  messageId: d.messageId, sequenceNumber: d.sequenceNumber, body: bodyOf(d, nsOf(w, d.namespaceId).provider), contentType: d.story === 'poison' ? 'text/html' : 'application/json', correlationId: d.correlationId, sessionId: null, subject: null,
  enqueuedTime: d.enqueuedTimeUtc, deliveryCount: d.deliveryCount, deadLetterReason: d.deadLetterReason, deadLetterErrorDescription: d.deadLetterErrorDescription, applicationProperties: propertiesOf(d), sizeInBytes: d.sizeInBytes, isFromDeadLetter: true,
})

/** Messages still waiting to be processed. Made from the queue's name and a counter, so the same queue always shows the same ones. */
function waiting(w: World, namespaceId: string, entity: string, now: number): Message[] {
  const found = w.entities[namespaceId]?.find((e) => e.name === entity)
  const count = Math.min(found?.activeMessages ?? 12, 60)
  return Array.from({ length: count }, (_, i): Message => {
    const n = (entity.length * 7919 + i * 104_729) % 100_000
    return {
      messageId: `live-${entity.replace(/\W/g, '')}-${i}`, sequenceNumber: 90_000 + entity.length * 1000 + i, body: JSON.stringify({ orderId: `ORD-${n}`, customerId: `CUS-${10_000 + (n % 89_999)}`, status: 'pending' }, null, 2),
      contentType: 'application/json', correlationId: `corr-live-${n}`, sessionId: null, subject: null, enqueuedTime: new Date(now - (i + 1) * 47_000).toISOString(), deliveryCount: 1,
      deadLetterReason: null, deadLetterErrorDescription: null, applicationProperties: { source: ['checkout', 'mobile-app', 'partner-api'][i % 3] }, sizeInBytes: 180 + (n % 200), isFromDeadLetter: false,
    }
  })
}

function peek(w: World, namespaceId: string, p: Record<string, unknown>, all: Message[], deadLetter: boolean): PeekPage {
  const ns = nsOf(w, namespaceId) ?? notFound('namespace')
  const max = Math.min(Number(p.max ?? 25), 100)
  const from = p.from === undefined ? null : Number(p.from)
  const rest = all.filter((m) => from === null || m.sequenceNumber >= from)
  const messages = rest.slice(0, max)
  const repeatable = ns.capabilities!.supportsRepeatablePeek
  return {
    namespaceId, entity: String(p.entity ?? ''), subscription: p.subscription ? String(p.subscription) : null, deadLetter, messages,
    paging: { requested: max, returned: messages.length, nextFromSequenceNumber: rest.length > max ? rest[max].sequenceNumber : null },
    peek: { repeatable, warning: repeatable ? null : 'On this cloud, looking at a message counts as a delivery attempt.' },
  }
}

export const messages: readonly Route[] = [
  ['get', /^\/namespaces\/([^/]+)\/messages\/peek$/, (m, { w, params, now }) => peek(w, m[1], params, waiting(w, m[1], entityName(params), now), false)],
  ['get', /^\/namespaces\/([^/]+)\/dead-letter\/peek$/, (m, { w, params }) => peek(w, m[1], params, activeIn(w, m[1], entityName(params)).map((d) => fromDeadLetter(w, d)), true)],
  ['get', /^\/namespaces\/([^/]+)\/messages\/scheduled$/, (m, { w, params, now }): ScheduledPage => {
    const ns = nsOf(w, m[1]) ?? notFound('namespace')
    if (!ns.capabilities!.supportsScheduledMessages) refuse(409, 'CAPABILITY_UNAVAILABLE', 'This cloud has no scheduled messages to list.')
    const entity = entityName(params)
    return {
      entity: String(params.entity ?? ''), subscription: params.subscription ? String(params.subscription) : null, capped: false,
      messages: [2, 9, 26].map((hours, i) => ({ messageId: `scheduled-${entity.replace(/\W/g, '')}-${i}`, sequenceNumber: 70_000 + entity.length * 10 + i, scheduledFor: new Date(now + hours * 3_600_000).toISOString(), sizeInBytes: 96 + i * 20, contentType: 'application/json', bodyPreview: JSON.stringify({ remind: ['invoice overdue', 'cart abandoned', 'trial ending'][i], attempt: i + 1 }) })),
    }
  }],
  ['get', /^\/namespaces\/([^/]+)\/messages\/(\d+)$/, (m, { w, params, now }) => {
    const sequence = Number(m[2])
    const dead = w.deadLetters.find((d) => d.namespaceId === m[1] && d.sequenceNumber === sequence)
    return (dead ? fromDeadLetter(w, dead) : waiting(w, m[1], entityName(params), now).find((x) => x.sequenceNumber === sequence)) ?? notFound('message')
  }],
  ['post', /^\/namespaces\/([^/]+)\/messages$/, (m, { w, body, now }) => {
    const found = w.entities[m[1]]?.find((e) => e.name === body.entity)
    if (found && found.activeMessages !== null) (found as { activeMessages: number }).activeMessages += 1
    audit(w, people.you, 'Message.Send', m[1], String(body.entity ?? ''), now)
    save()
    return { accepted: true, detail: DEMO_NOTE }
  }],
]
