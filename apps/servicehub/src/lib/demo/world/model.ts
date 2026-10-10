import type { Agent, AgentActivityItem } from '../../api/agents'
import type { BulkProgress } from '../../api/bulk'
import type { DeadLetter } from '../../api/deadLetters'
import type { AuditEntry } from '../../api/identity'
import type { InsightFinding } from '../../api/insights'
import type { CloudProvider, Entity, Namespace } from '../../api/namespaces'
import type { EntryState } from '../../api/recovery'
import type { ReplayActor } from '../../api/replay'
import type { EmergencyStop, Grant, NotificationChannel } from '../../api/settings'

/** The five failure stories every cloud tells (see the demo's seed). */
export type Story = 'outage' | 'token' | 'schema' | 'poison' | 'timeouts'

/** A dead letter, plus what the demo needs to know about it that the API never carries. */
export type DemoDeadLetter = { -readonly [K in keyof DeadLetter]: DeadLetter[K] } & {
  readonly story: Story
  readonly signatureHash: string
  readonly correlationId: string
}

/** One entry in the made-up recovery ledger. Replay rows and the ledger list are both read from these. */
export interface DemoEntry {
  readonly id: string
  readonly operationId: string
  readonly kind: 'Replay' | 'Purge' | 'WriteOff'
  readonly dlqMessageId: number
  readonly namespaceId: string
  readonly provider: CloudProvider
  readonly entityName: string
  readonly targetEntity: string
  readonly messageId: string
  readonly signatureHash: string
  readonly story: Story
  readonly begunAt: string
  readonly actor: ReplayActor
  readonly ruleId: number | null
  readonly level: 'approve' | 'standing' | 'unattended' | null
  state: EntryState
  confidence: 'Exact' | 'Heuristic' | null
  /** What the watch card shows: the real product watches for hours. */
  readonly windowEndsAt: string | null
  closedAt: string | null
  /** Demo time is sped up: when this entry's window really closes here. Null once closed, or for seeded history. */
  dueAt: number | null
}

/** An Auto Replay rule as stored. Everything it has done is counted from the ledger when it is read. */
export interface DemoRule {
  readonly id: number
  name: string
  readonly provider: CloudProvider
  readonly namespaceId: string
  readonly reason: string | null
  readonly entityName: string | null
  readonly signatureHash: string | null
  maxPerHour: number
  waitSeconds: number
  backOff: boolean
  enabled: boolean
  disabledReason: 'CircuitBreaker' | 'Person' | null
  disabledDetail: string | null
  updatedAt: string | null
}

export interface DemoEntity extends Entity {
  /** Messages waiting to be processed. Made up once; the dead-letter count is always counted from the rows. */
  readonly activeMessages: number | null
}

export interface DemoBulk extends BulkProgress {
  readonly ids: readonly number[]
  readonly purgeReason: string | null
}

/** The whole made-up world. Plain data, so it survives a refresh in sessionStorage. */
export interface World {
  readonly version: number
  readonly builtAt: number
  rng: number
  namespaces: Namespace[]
  entities: Record<string, DemoEntity[]>
  deadLetters: DemoDeadLetter[]
  entries: DemoEntry[]
  rules: DemoRule[]
  /** Dead letters a person declined to replay: the Agent stops asking about them. */
  declined: number[]
  agents: Agent[]
  activity: Record<string, AgentActivityItem[]>
  audit: AuditEntry[]
  channels: NotificationChannel[]
  emergencyStop: EmergencyStop
  grants: Grant[]
  insights: InsightFinding[]
  bulk: Record<string, DemoBulk>
  previews: Record<string, { ids: number[]; kind: 'replay' | 'purge'; reason: string | null }>
  /** Dead letters that have arrived on a cloud ServiceHub does not watch, and nobody has looked at yet. */
  unseen: Record<string, number>
  next: { deadLetter: number; entry: number; rule: number; audit: number; channel: number; grant: number; bulk: number }
  ticks: number
}
