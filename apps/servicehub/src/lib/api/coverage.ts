import { api } from './client'

/** A cloud as a heading. Nothing may decide behaviour from it — that comes from `class` and the flags below (R4). */
export type CloudKind = 'azure' | 'aws' | 'gcp'
export type SupportStatus = 'supported' | 'beingValidated' | 'onRequest' | 'notSupported'
export type DetectionKind = 'none' | 'store' | 'runs' | 'records' | 'signal'

/**
 * Whether a component is read on demand (a run history or a store of failed records) instead of watched like a message store. These are
 * the components a person ticks in Add a component, and the menu lists one only where it is on. The server decides it
 * (`ComponentAccessRules.ReadsOnDemand`); this is the one place the web asks the same question.
 */
export const readsOnDemand = (detection: DetectionKind): boolean => detection === 'runs' || detection === 'records'
export type RecoverabilityClass =
  | 'r0Unknown' | 'r1ReplayableProvable' | 'r2ReplayableUnproven' | 'r3CloudRedriveOnly' | 'r4PointerOnly' | 'r5DetectOnly' | 'r6NotRecoverable'

/**
 * One row of "What ServiceHub covers" (5.0.0): one component, one place work gets stuck. Everything that can be derived is
 * derived by the server (`ServiceHub.Core.Components.CoverageRow`), so no screen works it out again.
 */
export interface CoverageRow {
  readonly id: string
  /** Shared by a component's rows; the menu and the routes use it. */
  readonly componentKey: string
  readonly cloud: CloudKind
  readonly cloudName: string
  readonly name: string
  readonly stuckPlace: string
  readonly detection: DetectionKind
  readonly detectionWords: string
  readonly movesOnBy: string
  readonly status: SupportStatus
  readonly statusWords: string
  readonly note: string | null
  readonly class: RecoverabilityClass
  readonly offersReplay: boolean
  readonly offersCloudRedrive: boolean
  readonly mayEarnAutonomy: boolean
  readonly inSidebar: boolean
  readonly entityKinds: readonly string[]
  readonly planUnit: string | null
}

export interface CoverageResponse {
  readonly rows: readonly CoverageRow[]
}

/** The whole catalogue. It describes the product, not this installation, so it is the same for everyone. */
export async function fetchCoverage(): Promise<CoverageResponse> {
  return (await api.get<CoverageResponse>('/fleet/coverage')).data
}
