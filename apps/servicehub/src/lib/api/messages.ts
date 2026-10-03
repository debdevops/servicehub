import { api } from './client'
import { Intent, withIntent } from './intentHeaders'

/**
 * One message, peeked live. ServiceHub never stores a message body (ADR-0004): this is read from the
 * cloud when asked for.
 */
export interface Message {
  readonly messageId: string
  /** Azure's broker sequence number; on AWS/Google a stable hash of the id — it identifies, it does not order. */
  readonly sequenceNumber: number
  readonly body: string | null
  readonly contentType: string | null
  readonly correlationId: string | null
  readonly sessionId: string | null
  readonly subject: string | null
  readonly enqueuedTime: string
  readonly deliveryCount: number
  readonly deadLetterReason: string | null
  readonly deadLetterErrorDescription: string | null
  readonly applicationProperties: Readonly<Record<string, unknown>> | null
  readonly sizeInBytes: number
  readonly isFromDeadLetter: boolean
}

export interface PeekPaging {
  readonly requested: number
  readonly returned: number
  /** Pass as `from` for the next page. Null when there is no cursor (last page, or a cloud that cannot page). */
  readonly nextFromSequenceNumber: number | null
}

/**
 * What looking at messages does to this cloud — from its capabilities, never its name (R4). When
 * `repeatable` is false every peek is a real receive that counts as a delivery attempt, so the client
 * must not refresh it on a timer.
 */
export interface PeekSafety {
  readonly repeatable: boolean
  readonly warning: string | null
}

export interface PeekPage {
  readonly namespaceId: string
  readonly entity: string
  readonly subscription: string | null
  readonly deadLetter: boolean
  readonly messages: readonly Message[]
  readonly paging: PeekPaging
  readonly peek: PeekSafety
}

export interface PeekQuery {
  /** The queue, or the topic when `subscription` is given. */
  readonly entity: string
  readonly subscription?: string
  /** Page size, 1–100. The API defaults to 25. */
  readonly max?: number
  /** The previous page's `nextFromSequenceNumber`. Refused by clouds that cannot page. */
  readonly from?: number
}

const params = (q: PeekQuery) => ({ entity: q.entity, subscription: q.subscription, max: q.max, from: q.from })

/** A page of the messages waiting in a queue or subscription. */
export async function peekMessages(namespaceId: string, query: PeekQuery): Promise<PeekPage> {
  return (await api.get<PeekPage>(`/namespaces/${namespaceId}/messages/peek`, { params: params(query) })).data
}

/** A page of the dead-lettered messages of a queue or subscription. */
export async function peekDeadLetters(namespaceId: string, query: PeekQuery): Promise<PeekPage> {
  return (await api.get<PeekPage>(`/namespaces/${namespaceId}/dead-letter/peek`, { params: params(query) })).data
}

/**
 * One message by sequence number. Only clouds with a repeatable peek can answer; elsewhere the API
 * refuses (409 `capability_unavailable`) rather than receive the message to look at it.
 */
export async function fetchMessage(
  namespaceId: string,
  sequenceNumber: number,
  query: { entity: string; subscription?: string; deadLetter?: boolean },
): Promise<Message> {
  return (
    await api.get<Message>(`/namespaces/${namespaceId}/messages/${sequenceNumber}`, {
      params: { entity: query.entity, subscription: query.subscription, deadLetter: query.deadLetter },
    })
  ).data
}

/**
 * Whether a page may be refreshed on a timer. Reads the API's own answer about the cloud — a client
 * that asked "is this AWS?" instead would be re-implementing a capability from a name.
 */
export function canAutoRefresh(page: Pick<PeekPage, 'peek'> | undefined): boolean {
  return page?.peek.repeatable === true
}

export interface SendInput {
  readonly namespaceId: string
  readonly entity: string
  readonly isTopic: boolean
  readonly body: string
  readonly contentType?: string
  readonly properties?: Readonly<Record<string, string>>
}

/** Puts one new message onto a queue or topic (unit 6.14). Refused in Production. */
export async function sendMessage({ namespaceId, ...input }: SendInput): Promise<{ accepted: boolean; detail: string }> {
  return (await api.post<{ accepted: boolean; detail: string }>(`/namespaces/${namespaceId}/messages`, input, { headers: withIntent(Intent.SendMessage) })).data
}

export interface ScheduledMessage {
  readonly messageId: string
  readonly sequenceNumber: number
  readonly scheduledFor: string | null
  readonly sizeInBytes: number
  readonly contentType: string | null
  readonly bodyPreview: string | null
}

export interface ScheduledPage {
  readonly entity: string
  readonly subscription: string | null
  readonly messages: readonly ScheduledMessage[]
  /** True when the list stopped at the cap, so there may be more. */
  readonly capped: boolean
}

/** What is waiting to be delivered later (unit 6.17). A cloud without scheduled messages answers 409, never an empty list. */
export async function fetchScheduled(namespaceId: string, query: { entity: string; subscription?: string }): Promise<ScheduledPage> {
  return (await api.get<ScheduledPage>(`/namespaces/${namespaceId}/messages/scheduled`, { params: query })).data
}
