import { api } from './client'
import type { CloudKind, RecoverabilityClass } from './coverage'
import type { EnvironmentKind } from './namespaces'

/** How far a number can be trusted. A bare number might be mistaken for exact, so it never travels alone. */
export type CountKind = 'exact' | 'approximate' | 'notReported' | 'notRead'

/** `value` is null when the cloud reports none (`notReported`: say "Can't count here") or the read failed (`notRead`). */
export interface Count {
  readonly value: number | null
  readonly kind: CountKind
}

/** What ServiceHub recorded the last time it looked, on a cloud it does not watch. `messages` is null when it never looked. */
export interface LastLook {
  readonly messages: number | null
  /** True when the look stopped at its limit, so the real number may be higher. */
  readonly reachedLimit: boolean
  readonly atUtc: string | null
}

/** One place messages get stuck: a queue, a subscription, a subscriber queue. */
export interface ComponentEntity {
  readonly name: string
  /** The topic a subscription belongs to. */
  readonly parent: string | null
  readonly kind: string
  readonly deadLetterStore: string | null
  readonly noStoreReason: string | null
  readonly deadLetters: Count
  /** Null on a cloud that is watched automatically. */
  readonly recorded: LastLook | null
  readonly needsYou: number
  readonly observedAtUtc: string | null
}

/** One connected component: one top row on Home. `namespaceId` is the stored connection, `componentKey` which component of it. */
export interface ConnectedComponent {
  readonly namespaceId: string
  readonly componentKey: string
  readonly cloud: CloudKind
  readonly name: string
  readonly componentName: string
  readonly environment: EnvironmentKind
  readonly canSend: boolean
  readonly reachable: boolean
  readonly lastError: string | null
  /** True where ServiceHub looks by itself; false where a person presses Look now. */
  readonly watched: boolean
  readonly class: RecoverabilityClass
  readonly deadLetters: Count
  readonly recorded: LastLook | null
  readonly needsYou: number
  readonly approvals: number
  readonly handOffs: number
  readonly observedAtUtc: string | null
  readonly entities: readonly ComponentEntity[]
}

export interface ComponentOverview {
  readonly components: readonly ConnectedComponent[]
  readonly generatedAtUtc: string
}

/** Every connected component the caller may see, for one cloud or (omitted) all. Totals are per cloud, never added across clouds. */
export async function fetchComponents(cloud?: CloudKind): Promise<ComponentOverview> {
  return (await api.get<ComponentOverview>('/fleet/components', { params: cloud ? { cloud } : undefined })).data
}

/** One component with all of its entities. */
export async function fetchComponent(namespaceId: string, componentKey: string): Promise<ConnectedComponent> {
  return (await api.get<ConnectedComponent>(`/fleet/components/${namespaceId}/${componentKey}`)).data
}
