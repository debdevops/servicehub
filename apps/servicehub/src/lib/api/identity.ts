import { api } from './client'
import type { CloudProvider, EnvironmentKind } from './namespaces'

/** An actor as a person reads it. */
export interface Actor {
  /** The identity string stored on audit rows. */
  readonly identity: string
  readonly kind: 'user' | 'apiKey' | 'automation' | 'system'
  /** The words to show: a name, an `ApiKey:` credential, or "from this browser session". */
  readonly label: string
  /** True when the actor is known only as a browser session — never dress it up as a person. */
  readonly isSession: boolean
}

/** Who ServiceHub believes is asking, and how it knows. */
export interface Me {
  readonly ownerId: string
  /** `session` when nothing is configured; otherwise `EasyAuth`, `Oidc` or `ApiKey`. */
  readonly authMethod: string
  readonly actor: Actor
  /** The fleet-wide governance role — Admin while governance is inactive. Null: governance is on and nothing is granted. */
  readonly effectiveRole: Role | null
  /** True once any grant has ever existed; until then everyone is Admin. */
  readonly governanceActive?: boolean
  /** Who can grant roles (Admins), as people read them. */
  readonly grantors?: readonly string[]
  /** The role for recovery actions (replay, approve…) fleet-wide, and per namespace — a namespace grant can add to it. */
  readonly recoverRole?: Role | null
  readonly namespaceRecoverRoles?: Readonly<Record<string, Role | null>>
}

export type Role = 'Viewer' | 'Operator' | 'Approver' | 'Admin'

export async function fetchMe(): Promise<Me> {
  return (await api.get<Me>('/me')).data
}

export interface AuditEntry {
  readonly id: string
  readonly timestamp: string
  readonly actor: Actor
  /** A stable name such as `Namespace.Connect`. */
  readonly action: string
  readonly outcome: 'Success' | 'Failure'
  readonly namespaceId: string | null
  readonly namespaceName: string | null
  readonly cloudProvider: string | null
  readonly environment: string | null
  readonly resourceName: string | null
  readonly errorDetails: string | null
  readonly correlationId: string | null
}

export interface AuditPage {
  readonly items: readonly AuditEntry[]
  readonly page: number
  readonly pageSize: number
  readonly total: number
}

export interface AuditFilter {
  readonly page?: number
  readonly pageSize?: number
  readonly namespaceId?: string
  readonly provider?: CloudProvider
  readonly environment?: EnvironmentKind
  readonly action?: string
}

/** The durable history Home's Recent Activity reads, newest first. */
export async function fetchAudit(filter: AuditFilter = {}): Promise<AuditPage> {
  return (await api.get<AuditPage>('/audit', { params: { ...filter, provider: filter.provider ? { azure: 'Azure', aws: 'Aws', gcp: 'Gcp' }[filter.provider] : undefined } })).data
}
