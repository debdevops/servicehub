import { api } from './client'
import { Intent, withIntent } from './intentHeaders'
import type { CloudProvider, EnvironmentKind } from './namespaces'

export type CheckState = 'passed' | 'warning' | 'blocked'

export interface ReplayCheck {
  /** `status` · `environment` · `frequency` · `verification`. */
  readonly id: string
  readonly label: string
  readonly state: CheckState
  readonly detail: string | null
}

/**
 * What replaying one dead letter would do, shown BEFORE it runs. Only checks that were evaluated appear:
 * no rate limit and no "target queue exists" line, because nothing yet measures them.
 */
export interface ReplayProposal {
  readonly dlqMessageId: number
  readonly messageId: string
  readonly sourceEntity: string
  readonly targetEntity: string
  readonly namespaceName: string
  readonly provider: CloudProvider
  readonly environment: string
  readonly stampsRecoveryMarker: boolean
  readonly priorAttempts: number
  readonly attemptCap: number
  readonly othersLikeIt: number
  readonly observationWindowHours: number
  /** Whether this cloud can prove the queue stayed empty (R4). */
  readonly canConfirm: boolean
  readonly verdict: 'Allow' | 'Escalate' | 'Deny'
  /** The gate's named reason — a code with a remedy, never prose. */
  readonly reasonCode: string | null
  readonly approvable: boolean
  readonly canExecute: boolean
  readonly blockedCode: string | null
  readonly checks: readonly ReplayCheck[]
}

export interface ReplayOutcome {
  readonly entryId: string
  readonly operationId: string
  readonly result: 'accepted' | 'rejected' | 'unknown'
  readonly state: string
  readonly markerApplied: boolean
  readonly observationWindowEndsAt: string | null
  readonly message: string
  readonly errorCode: string | null
}

/**
 * The honest answer to "did it work?" — the same shape on every cloud (C5). `verified` appears only where the
 * cloud can prove the queue stayed empty; `verification_required` is NOT a failure, it means the confirmation
 * is unproven, and it carries the remedy.
 */
export type VerificationStatus = 'watching' | 'verified' | 'verification_required' | 'returned' | 'not_sent' | 'unknown'

export interface ReplayVerification {
  readonly status: VerificationStatus
  readonly reasonCode: string | null
  /** For `returned`: `Exact` (matched by the recovery ID) or `Heuristic` (matched by contents). */
  readonly confidence: 'Exact' | 'Heuristic' | null
  readonly watchUntil: string | null
  readonly canConfirm: boolean
  /** `SETUP_DLQ_OBSERVER` when that is the way to a verified result. */
  readonly remedy: string | null
}

export interface ReplayActor {
  readonly identity: string
  readonly kind: 'user' | 'apiKey' | 'automation' | 'system'
  /** The words to show. For a browser session it is "from this browser session" — never a name. */
  readonly label: string
  readonly isSession: boolean
}

export interface ReplayListItem {
  readonly id: number
  readonly dlqMessageId: number
  readonly namespaceId: string
  readonly provider: CloudProvider
  readonly messageId: string
  readonly sourceEntity: string
  readonly targetEntity: string
  readonly replayedAt: string
  readonly replayedBy: string
  readonly actor: ReplayActor
  readonly outcomeStatus: 'accepted' | 'rejected' | 'unknown'
  readonly entryState: string | null
  readonly observationWindowEndsAt: string | null
  readonly markerApplied: boolean
  readonly verification: ReplayVerification
}

export interface ReplayPage {
  readonly items: readonly ReplayListItem[]
  readonly total: number
  readonly page: number
  readonly pageSize: number
}

export async function fetchReplayProposal(dlqMessageId: number): Promise<ReplayProposal> {
  return (await api.get<ReplayProposal>(`/dead-letters/${dlqMessageId}/replay-proposal`)).data
}

/** The one execution route. Carries the intent header; a stray request without it is refused. */
export async function replayMessage(dlqMessageId: number): Promise<ReplayOutcome> {
  return (await api.post<ReplayOutcome>(`/dead-letters/${dlqMessageId}/replay`, null, { headers: withIntent(Intent.ReplayMessage) })).data
}

/** Deletes one dead letter for good (unit 6.15) — through the gate and the ledger, with the reason recorded. */
export async function purgeMessage(dlqMessageId: number, reason: string): Promise<ReplayOutcome> {
  return (await api.post<ReplayOutcome>(`/dead-letters/${dlqMessageId}/purge`, { reason }, { headers: withIntent(Intent.PurgeMessage) })).data
}

const providerParam = (p: CloudProvider) => ({ azure: 'Azure', aws: 'Aws', gcp: 'Gcp' })[p]

export interface ReplayQuery {
  readonly provider?: CloudProvider
  readonly namespaceId?: string
  /** Only namespaces of this environment — the Environment level of the scope picker. */
  readonly environment?: EnvironmentKind
  /** Only the replays of this one dead letter (the drawer). */
  readonly dlqMessageId?: number
  readonly result?: 'accepted' | 'rejected' | 'unknown'
  readonly page?: number
  readonly pageSize?: number
}

export async function fetchReplays(query: ReplayQuery): Promise<ReplayPage> {
  return (await api.get<ReplayPage>('/replays', { params: { ...query, provider: query.provider ? providerParam(query.provider) : undefined } })).data
}
