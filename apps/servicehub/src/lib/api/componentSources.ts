import { api } from './client'

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
