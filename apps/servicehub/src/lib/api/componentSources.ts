import { api } from './client'
import { Intent, withIntent } from './intentHeaders'

/**
 * A component that is not a message store: failed runs or failed deliveries (5.0.0, PLAN-PLUG-AND-PLAY-COMPONENTS). Everything here is
 * read-only. Nothing in these shapes can be sent back to a cloud, and there is no action to take on them in ServiceHub.
 */
export interface SourcePlace {
  readonly id: string
  readonly name: string
  /** The cloud's own word for it, such as "state machine". */
  readonly kind: string
  readonly consoleUrl: string | null
}

/** Whether this connection can read the component. `available` false carries the reason; it is a state, not an error. */
export interface ComponentSource {
  readonly componentKey: string
  readonly available: boolean
  readonly reason: string | null
  /** False when the list does not carry each item's reason; it is read per item, on demand. */
  readonly reasonInList: boolean
  readonly places: readonly SourcePlace[]
  /** More places exist than were returned. */
  readonly placesReachedLimit: boolean
  /** Part of the places read failed or timed out, so a place that exists may be missing. */
  readonly placesIncomplete: boolean
}

export interface FailedItem {
  readonly id: string
  readonly place: string
  readonly atUtc: string | null
  /** The cloud's own status word, such as FAILED or TIMED_OUT. */
  readonly status: string
  readonly reasonCode: string | null
  readonly summary: string
  readonly copy: 'none' | 'pointer' | 'held'
  readonly consoleUrl: string | null
  /** Another component's entity that also holds this failure and can put it back (Eventarc: its Pub/Sub transport subscription), or null/absent. */
  readonly heldIn?: { readonly componentKey: string; readonly entity: string } | null
}

export interface FailedItems {
  readonly componentKey: string
  readonly items: readonly FailedItem[]
  /** More exist than were returned. */
  readonly reachedLimit: boolean
  /** Part of the read failed or ran out of time, so items that exist may be missing. */
  readonly incomplete: boolean
}

export interface FailedItemDetail {
  /** Null when the cloud no longer has the item. */
  readonly item: FailedItem | null
  readonly reason: string | null
  readonly cause: string | null
  readonly startedUtc: string | null
  readonly stoppedUtc: string | null
}

export async function fetchComponentSource(namespaceId: string, componentKey: string): Promise<ComponentSource> {
  return (await api.get<ComponentSource>(`/namespaces/${namespaceId}/components/${componentKey}/source`)).data
}

export async function fetchFailedItems(namespaceId: string, componentKey: string, place: string | null): Promise<FailedItems> {
  return (await api.get<FailedItems>(`/namespaces/${namespaceId}/components/${componentKey}/failed`, { params: place ? { place } : undefined })).data
}

export async function fetchFailedItem(namespaceId: string, componentKey: string, itemId: string): Promise<FailedItemDetail> {
  return (await api.get<FailedItemDetail>(`/namespaces/${namespaceId}/components/${componentKey}/failed/detail`, { params: { itemId } })).data
}

/** Whether a person may ask the cloud to redrive (or resubmit) one failed run now, and if not, why. Asks the server's gate; changes nothing. */
export interface RedriveCheck {
  readonly canRedrive: boolean
  readonly reasonCode: string | null
  readonly message: string
  /** What the cloud calls the action: `redrive` or `resubmit`. */
  readonly verb?: string
  /** When the cloud last accepted this same run's request, or null/absent when it never did. A warning, never a block. */
  readonly previousRequestAtUtc?: string | null
}

/** What a redrive request ended as: what the cloud accepted, and nothing more. The run's own result is never known. */
export interface RedriveOutcome {
  readonly entryId: string
  readonly operationId: string
  readonly result: 'accepted' | 'rejected' | 'unknown'
  readonly state: string
  readonly message: string
  readonly errorCode: string | null
}

export async function fetchRedriveCheck(namespaceId: string, componentKey: string, itemId: string): Promise<RedriveCheck> {
  return (await api.get<RedriveCheck>(`/namespaces/${namespaceId}/component-redrives/${componentKey}/check`, { params: { itemId } })).data
}

/** The one route that asks a cloud to redrive a run. Carries the intent header; Approver only; never retried. */
export async function redriveRun(namespaceId: string, componentKey: string, itemId: string): Promise<RedriveOutcome> {
  return (await api.post<RedriveOutcome>(`/namespaces/${namespaceId}/component-redrives/${componentKey}`, null, { params: { itemId }, headers: withIntent(Intent.RedriveExecution) })).data
}
