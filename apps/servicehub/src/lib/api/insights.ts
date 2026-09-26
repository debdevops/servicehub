import { api } from './client'
import type { CloudProvider } from './namespaces'

export type InsightKind = 'anomaly' | 'backlog' | 'correlation' | 'narration'

export interface InsightFinding {
  readonly id: number
  readonly kind: InsightKind
  readonly severity: number
  readonly what: string
  readonly entityName: string | null
  readonly namespaceId: string | null
  readonly namespaceName: string | null
  readonly provider: CloudProvider | null
  readonly metrics: Readonly<Record<string, number>> | null
  readonly firstSeenAt: string
  readonly lastSeenAt: string
  readonly clearedAt: string | null
  /** Templated narration — a suggestion, never a system finding (R3). */
  readonly suggestion: boolean
}

export interface InsightList {
  readonly current: readonly InsightFinding[]
  readonly cleared: readonly InsightFinding[]
  readonly lastLookedAt: string | null
}

export async function fetchInsights(q: { provider?: CloudProvider; cleared?: boolean }): Promise<InsightList> {
  const provider = q.provider ? ({ azure: 'Azure', aws: 'Aws', gcp: 'Gcp' } as const)[q.provider] : undefined
  return (await api.get<InsightList>('/insights', { params: { provider, cleared: q.cleared || undefined } })).data
}
