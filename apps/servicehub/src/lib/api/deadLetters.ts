import { api } from './client'
import type { CloudProvider } from './namespaces'

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
  readonly status: string
}

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

export interface DeadLetterQuery {
  readonly provider?: CloudProvider
  readonly namespaceId?: string
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
