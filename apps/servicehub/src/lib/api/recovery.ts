import { api } from './client'
import type { CloudProvider, EnvironmentKind } from './namespaces'
import type { ReplayActor } from './replay'

/** The enum's own names, in lifecycle order. `Recovered` means "did not return" — never "succeeded" (V2). */
export type EntryState =
  | 'Executing' | 'Observing' | 'ExecutionFailed' | 'ExecutionUnknown' | 'Recovered'
  | 'Returned' | 'Discarded' | 'Unverified' | 'WrittenOff' | 'Expired' | 'Declined'

export type RecoveryWindow = '24h' | '7d' | '30d' | 'all'

export interface StateCount {
  readonly state: EntryState
  /** Zero is a real answer (V4): "0 failed" is information, an absent row is ambiguity. */
  readonly count: number
}

export interface ProviderSummary {
  readonly provider: CloudProvider
  readonly total: number
  readonly states: readonly StateCount[]
  readonly stayedFixedRate: number | null
}

/**
 * How did recoveries end? Computed once by the server, so Simple's percentage and Advanced's breakdown can never
 * disagree. Recovered and Unverified are never merged (V1); a rate with nothing to divide is `null`, never 0.
 */
export interface RecoverySummary {
  readonly window: RecoveryWindow
  readonly total: number
  readonly states: readonly StateCount[]
  readonly byProvider: readonly ProviderSummary[]
  readonly stayedFixedRate: number | null
  readonly returnedConfidence: { readonly exact: number; readonly heuristic: number }
  readonly replaysAccepted: number
}

export interface RecoveryScope {
  readonly window?: RecoveryWindow
  readonly provider?: CloudProvider
  readonly namespaceId?: string
  /** Only entries made in this environment — the Environment level of the scope picker. */
  readonly environment?: EnvironmentKind
}

const providerParam = (p: CloudProvider) => ({ azure: 'Azure', aws: 'Aws', gcp: 'Gcp' })[p]
const scopeParams = (q: RecoveryScope) => ({ window: q.window, provider: q.provider ? providerParam(q.provider) : undefined, namespaceId: q.namespaceId, environment: q.environment })

export async function fetchRecoverySummary(scope: RecoveryScope = {}): Promise<RecoverySummary> {
  return (await api.get<RecoverySummary>('/recovery/summary', { params: scopeParams(scope) })).data
}

export interface LedgerEntry {
  readonly id: string
  readonly operationId: string
  readonly begunAt: string
  readonly kind: 'Replay' | 'Purge' | string
  readonly entityName: string
  readonly targetEntity: string
  readonly provider: CloudProvider | null
  readonly namespaceName: string | null
  readonly actor: ReplayActor
  readonly state: EntryState
  /** For `Returned`: `Exact` (matched by the recovery ID) or `Heuristic` (matched by contents, may be shared). */
  readonly confidence: 'Exact' | 'Heuristic' | null
  readonly dlqMessageId: number | null
  readonly closedAt: string | null
}

export interface LedgerPage {
  readonly items: readonly LedgerEntry[]
  readonly total: number
  readonly page: number
  readonly pageSize: number
}

export interface LedgerEvent {
  readonly seq: number
  readonly eventType: string
  readonly occurredAt: string
  readonly actor: ReplayActor
  readonly detail: string | null
  readonly prevHash: string
  readonly entryHash: string
}

export interface LedgerEntryDetail {
  readonly entry: LedgerEntry
  readonly recoveryMarker: string | null
  readonly markerApplied: boolean
  readonly deadLetterReason: string | null
  readonly verificationResult: string | null
  readonly observationWindowEndsAt: string | null
  readonly lastEventSeq: number
  readonly events: readonly LedgerEvent[]
}

export interface ChainVerification {
  readonly ownerId: string
  readonly isValid: boolean
  readonly eventsChecked: number
  readonly firstDivergentSeq: number | null
  readonly reason: string | null
}

export interface LedgerQuery extends RecoveryScope {
  readonly state?: EntryState
  readonly page?: number
  readonly pageSize?: number
}

export async function fetchLedger(query: LedgerQuery): Promise<LedgerPage> {
  return (await api.get<LedgerPage>('/recovery/entries', { params: { ...scopeParams(query), state: query.state, page: query.page, pageSize: query.pageSize } })).data
}

export async function fetchLedgerEntry(id: string): Promise<LedgerEntryDetail> {
  return (await api.get<LedgerEntryDetail>(`/recovery/entries/${id}`)).data
}

/** Read-only: recomputes the chain and reports. Never repairs. */
export async function verifyChain(): Promise<ChainVerification> {
  return (await api.get<ChainVerification>('/recovery/chain')).data
}

const windowMs: Readonly<Record<RecoveryWindow, number | null>> = { '24h': 864e5, '7d': 7 * 864e5, '30d': 30 * 864e5, all: null }

/**
 * The ledger as one file the offline verifier checks (unit 6.12). Always the whole chain inside the window — never narrowed to
 * a cloud or namespace, because leaving events out would break the chain. Resolves to the file name it saved.
 */
export async function exportEvidence(window: RecoveryWindow, now = new Date()): Promise<string> {
  const ms = windowMs[window]
  const from = ms === null ? undefined : new Date(now.getTime() - ms).toISOString()
  const res = await api.get<Blob>('/recovery/export', { params: { from }, responseType: 'blob' })
  const name = /filename="?([^";]+)"?/.exec(String(res.headers['content-disposition'] ?? ''))?.[1] ?? 'servicehub-evidence.json'
  const url = URL.createObjectURL(res.data)
  const a = Object.assign(document.createElement('a'), { href: url, download: name })
  a.click()
  URL.revokeObjectURL(url)
  return name
}
