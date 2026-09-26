import { api } from './client'
import type { CloudProvider, EnvironmentKind } from './namespaces'

/**
 * One dead letter ServiceHub has seen — the durable list, not a live peek. There is no body here:
 * a list is not the place for one, and search never looks inside one (R5).
 */
export interface DeadLetter {
  readonly id: number
  readonly namespaceId: string
  readonly messageId: string
  readonly sequenceNumber: number
  readonly entityName: string
  readonly entityType: 'queue' | 'subscription'
  readonly topicName: string | null
  /** When ServiceHub first saw it dead-lettered — the truthful proxy for "when", since no cloud reports the real time. */
  readonly detectedAtUtc: string
  readonly enqueuedTimeUtc: string
  readonly deliveryCount: number
  readonly sizeInBytes: number
  /** What the cloud or the application recorded. A recorded fact, never a guess. */
  readonly deadLetterReason: string | null
  readonly deadLetterErrorDescription: string | null
  readonly status: DeadLetterStatus
  /** When it left the queue, as recorded (unit 6.11). Null while it is still there. */
  readonly resolvedAt?: string | null
  /** What is known about how it left — recorded by whatever saw it happen, never inferred (R5). */
  readonly resolutionCause?: ResolutionCause | null
}

/** The API's lifecycle words, camelCase as it sends them (a PascalCase compare once hid Replay for every message). */
export type DeadLetterStatus = 'active' | 'replayed' | 'archived' | 'discarded' | 'replayFailed' | 'resolved' | 'replaying' | 'purging'
export type ResolutionCause = 'replayedByServiceHub' | 'purgedByServiceHub' | 'vanishedExternally' | 'declaredByOperator' | 'unknown'

export interface ReasonGroup {
  /** Null when no reason was recorded — said plainly, not hidden. */
  readonly reason: string | null
  readonly count: number
}

export interface DeadLetterPage {
  readonly items: readonly DeadLetter[]
  readonly paging: { readonly total: number; readonly page: number; readonly pageSize: number }
  /** Over everything the filters leave EXCEPT the reason, so the chips add up and picking one hides none. */
  readonly groups: readonly ReasonGroup[]
  readonly otherReasons: { readonly count: number; readonly kinds: number } | null
  readonly entities: readonly string[]
}

export type DeadLetterRange = '24h' | '7d' | '30d' | 'all'

/** Narrows a cloud's trend to one namespace or one environment. */
export interface TrendScope {
  readonly namespaceId?: string
  readonly environment?: EnvironmentKind
}

export interface DeadLetterQuery {
  readonly provider?: CloudProvider
  readonly namespaceId?: string
  /** Only namespaces of this environment — the Environment level of the scope picker. */
  readonly environment?: EnvironmentKind
  readonly status?: 'active' | 'resolved' | 'all'
  readonly range?: DeadLetterRange
  readonly reason?: string
  readonly noReason?: boolean
  readonly entity?: string
  readonly q?: string
  readonly page?: number
  readonly pageSize?: number
}

/** The API binds the enum by name; the SPA holds it lower-case. This is the one place they meet. */
const providerParam = (p: CloudProvider) => ({ azure: 'Azure', aws: 'Aws', gcp: 'Gcp' })[p]

export async function fetchDeadLetters(query: DeadLetterQuery): Promise<DeadLetterPage> {
  const { provider, ...rest } = query
  const params = {
    ...rest,
    provider: provider ? providerParam(provider) : undefined,
    // Omit what is off, so a URL and a request read the same.
    noReason: query.noReason ? true : undefined,
    range: query.range === 'all' ? undefined : query.range,
  }
  return (await api.get<DeadLetterPage>('/dead-letters', { params })).data
}

/**
 * One dead letter opened. The body is only the preview ServiceHub stored (the first 500 characters) —
 * `bodyIsPreview` says when there is more, so it is never shown as the whole message.
 */
export interface DeadLetterDetail {
  readonly item: DeadLetter
  readonly bodyPreview: string | null
  readonly bodyIsPreview: boolean
  readonly contentType: string | null
  readonly correlationId: string | null
  readonly sessionId: string | null
  /** The application properties, as stored JSON text. */
  readonly applicationPropertiesJson: string | null
  readonly resolvedAt: string | null
  /** Other active dead letters in this queue with the same recorded reason. */
  readonly othersLikeIt: number
}

export async function fetchDeadLetter(id: number): Promise<DeadLetterDetail> {
  return (await api.get<DeadLetterDetail>(`/dead-letters/${id}`)).data
}

export interface TrendDay {
  /** The UTC day, `yyyy-MM-dd`. */
  readonly date: string
  /** First seen dead-lettered that day. */
  readonly new: number
  /** Seen to leave the queue that day (replayed, or gone). */
  readonly resolved: number
}

export interface DeadLetterTrend {
  readonly days: number
  /** Every day of the range, oldest first, zeros included. Daily — no hourly series exists. */
  readonly series: readonly TrendDay[]
}

export async function fetchDeadLetterTrend(provider: CloudProvider, days: number, narrow: TrendScope = {}): Promise<DeadLetterTrend> {
  return (await api.get<DeadLetterTrend>('/dead-letters/trend', { params: { provider: providerParam(provider), days, namespaceId: narrow.namespaceId, environment: narrow.environment } })).data
}
