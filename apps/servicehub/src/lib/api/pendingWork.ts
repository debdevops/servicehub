import { api } from './client'
import { Intent, withIntent } from './intentHeaders'
import type { CloudProvider, EnvironmentKind } from './namespaces'
import type { ReplayOutcome } from './replay'

/** One thing waiting for a person. `reason` is the plain sentence; `reasonCode` travels beside it, never flattened. */
export interface PendingWorkItem {
  readonly kind: 'approval' | 'rule' | 'agent'
  readonly id: string
  readonly entryId: string | null
  readonly agentId: string | null
  readonly dlqMessageId: number | null
  readonly namespaceId: string | null
  readonly namespaceName: string | null
  readonly provider: CloudProvider | null
  readonly environment: EnvironmentKind | null
  readonly entity: string | null
  readonly deadLetterReason: string | null
  readonly ruleId: number | null
  readonly ruleName: string | null
  readonly reasonCode: string
  readonly reason: string
  readonly since: string
}

export interface PendingWorkPage {
  readonly items: readonly PendingWorkItem[]
  readonly total: number
  readonly byProvider: readonly { readonly provider: CloudProvider; readonly count: number }[]
  readonly agents: number
}

export interface PendingWorkScope {
  readonly provider?: CloudProvider
  readonly namespaceId?: string
  readonly environment?: EnvironmentKind
  readonly reason?: string
}

const providerParam = (p: CloudProvider) => ({ azure: 'Azure', aws: 'Aws', gcp: 'Gcp' })[p]

export async function fetchPendingWork(scope: PendingWorkScope = {}): Promise<PendingWorkPage> {
  const { provider, ...rest } = scope
  return (await api.get<PendingWorkPage>('/pending-work', { params: { ...rest, provider: provider ? providerParam(provider) : undefined } })).data
}

/** Yes: replays through the one gated route, as you. */
export async function approvePending(entryId: string): Promise<ReplayOutcome> {
  return (await api.post<ReplayOutcome>(`/pending-work/${entryId}/approve`, undefined, { headers: withIntent(Intent.ApproveEscalation) })).data
}

/** No: recorded with your name and reason. Deletes nothing. */
export async function declinePending(entryId: string, reason: string): Promise<void> {
  await api.post(`/pending-work/${entryId}/decline`, { reason }, { headers: withIntent(Intent.DeclineEscalation) })
}
