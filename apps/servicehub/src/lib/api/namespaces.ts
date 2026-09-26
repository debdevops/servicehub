import { api } from './client'
import { Intent, withIntent } from './intentHeaders'

export type CloudProvider = 'azure' | 'aws' | 'gcp'
export type EnvironmentKind = 'dev' | 'uat' | 'prod'
export type AuthType =
  | 'connectionString'
  | 'managedIdentity'
  | 'servicePrincipal'
  | 'defaultAzureCredential'
  | 'awsAccessKey'
  | 'awsIamRole'
  | 'awsOidc'
  | 'gcpServiceAccount'
  | 'gcpWorkloadIdentity'
export type EntityKind = 'queue' | 'topic' | 'subscription'

/**
 * What a provider genuinely supports. It arrives on every namespace, so the UI asks it "can this do
 * X?" and never derives the answer from the provider's name (rule R4).
 */
export interface ProviderCapabilities {
  readonly supportsMessageCounts: boolean
  readonly supportsManualDeadLetter: boolean
  readonly supportsPurge: boolean
  readonly supportsScheduledMessages: boolean
  readonly supportsRepeatablePeek: boolean
  readonly supportsRecoveryMarker: boolean
  readonly canProveDlqAbsence: boolean
  readonly supportsTopics: boolean
  readonly supportsSubscriptions: boolean
  readonly notes: string
}

/**
 * A connected namespace. There is no connection string on this type because the API never sends
 * one — not masked, not hashed.
 */
export interface Namespace {
  readonly id: string
  readonly name: string
  readonly displayName: string | null
  readonly description: string | null
  readonly provider: CloudProvider
  readonly environment: EnvironmentKind
  readonly authType: AuthType
  readonly awsRegion: string | null
  readonly gcpProjectId: string | null
  readonly isActive: boolean
  readonly createdAt: string
  readonly lastConnectionTestAt: string | null
  readonly lastConnectionTestSucceeded: boolean | null
  /** Null only when this build has no adapter for the provider. */
  readonly capabilities: ProviderCapabilities | null
}

export interface ConnectNamespaceInput {
  readonly name: string
  readonly provider: CloudProvider
  readonly authType: AuthType
  /** The credential. Sent once, on connect; the API never returns it. */
  readonly connectionString?: string
  readonly displayName?: string
  readonly description?: string
  readonly environment?: EnvironmentKind
  readonly awsRegion?: string
  readonly gcpProjectId?: string
}

export interface ConnectionTest {
  readonly isConnected: boolean
  readonly message: string
  readonly testedAt: string
}

export interface EntityKindCount {
  readonly kind: EntityKind
  readonly count: number
}

/**
 * Message totals are `null` — not `0` — when the provider cannot count messages. "Empty" and
 * "cannot know" are different answers (rule R5); a screen must render them differently.
 */
export interface NamespaceStats {
  readonly namespaceId: string
  readonly entities: readonly EntityKindCount[]
  readonly activeMessages: number | null
  readonly deadLetterMessages: number | null
  readonly messageCountsSupported: boolean
  readonly observedAt: string
}

export interface Entity {
  readonly name: string
  readonly kind: EntityKind
  readonly activeMessages: number | null
  readonly deadLetterMessages: number | null
  readonly deadLetterTargetName: string | null
}

export interface EntityList {
  readonly namespaceId: string
  readonly entities: readonly Entity[]
}

export async function fetchNamespaces(): Promise<Namespace[]> {
  return (await api.get<Namespace[]>('/namespaces')).data
}

export async function fetchNamespace(id: string): Promise<Namespace> {
  return (await api.get<Namespace>(`/namespaces/${id}`)).data
}

export async function connectNamespace(input: ConnectNamespaceInput): Promise<Namespace> {
  return (await api.post<Namespace>('/namespaces', input, { headers: withIntent(Intent.CreateNamespace) })).data
}

export async function testConnection(id: string): Promise<ConnectionTest> {
  return (await api.post<ConnectionTest>(`/namespaces/${id}/test-connection`)).data
}

export async function removeNamespace(id: string): Promise<void> {
  await api.delete(`/namespaces/${id}`, { headers: withIntent(Intent.DeleteNamespace) })
}

export async function fetchNamespaceStats(id: string): Promise<NamespaceStats> {
  return (await api.get<NamespaceStats>(`/namespaces/${id}/stats`)).data
}

/** One endpoint for every kind: which kinds exist is a capability, not a route. */
export async function fetchEntities(id: string, kind?: EntityKind): Promise<EntityList> {
  return (await api.get<EntityList>(`/namespaces/${id}/entities`, { params: kind ? { kind } : undefined })).data
}

/** What one "look now" found. `countsAsDeliveryAttempt` is true where looking was a receive (no repeatable peek). */
export interface DeadLetterLook {
  readonly outcome: 'looked' | 'failed'
  readonly queuesExamined: number
  readonly newMessages: number
  readonly resolved: number
  readonly unconfirmed: number
  readonly countsAsDeliveryAttempt: boolean
  readonly reason: string | null
  readonly lookedAtUtc: string
}

/**
 * Looks at a namespace's dead letters now and records them. On AWS and Google Cloud this is the only way they
 * reach the list — ServiceHub never looks there on its own, because a look counts as a delivery attempt.
 * A slow cloud can take tens of seconds per queue (SQS long-polls), hence the longer timeout.
 */
export async function lookAtDeadLetters(id: string): Promise<DeadLetterLook> {
  return (await api.post<DeadLetterLook>(`/namespaces/${id}/dead-letters/look`, undefined, { headers: withIntent(Intent.LookAtDeadLetters), timeout: 180_000 })).data
}
