import { api } from './client'
import type { CloudProvider, EnvironmentKind } from './namespaces'

export type FleetWindow = 'today' | '24h' | '7d'
export type FleetHealth = 'healthy' | 'needsALook' | 'cannotTell'

/** One cloud's own numbers. Nothing here is added across clouds (R5). */
export interface FleetCloud {
  readonly provider: CloudProvider
  readonly namespaceCount: number
  /** Computed by the server from `CanProveDlqAbsence` — never from the provider's name (R4). */
  readonly capability: 'canConfirm' | 'observerRequired'
  /** False where ServiceHub does not look on its own: the counts below are then absent, not zero. */
  readonly watched: boolean
  readonly active: number | null
  readonly newInWindow: number
  readonly resolvedInWindow: number
}

export interface FleetFailure {
  readonly reason: string
  readonly count: number
}

export interface FleetNamespace {
  readonly id: string
  readonly name: string
  readonly displayName: string | null
  readonly provider: CloudProvider
  readonly environment: EnvironmentKind
  readonly watched: boolean
  readonly active: number | null
  readonly newInWindow: number
  readonly resolvedInWindow: number
  readonly topFailure: FleetFailure | null
  readonly health: FleetHealth
}

export interface FleetTopFailure {
  readonly provider: CloudProvider
  readonly environment: EnvironmentKind
  readonly reason: string
  readonly count: number
}

export interface FleetOverview {
  readonly window: FleetWindow
  readonly since: string
  readonly clouds: readonly FleetCloud[]
  readonly namespaces: readonly FleetNamespace[]
  readonly topFailures: readonly FleetTopFailure[]
}

export async function fetchFleet(window: FleetWindow): Promise<FleetOverview> {
  return (await api.get<FleetOverview>('/fleet/overview', { params: { window } })).data
}
