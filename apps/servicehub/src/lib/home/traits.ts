import type { FleetCloud } from '../api/fleet'
import type { Namespace, ProviderCapabilities } from '../api/namespaces'

/**
 * What a cloud (one namespace, or every namespace of one provider in scope) can actually do — the one
 * thing Home's layout branches on (D48, R4). Never test `provider === 'aws'`: test `traits.watched`,
 * `traits.counts`, `traits.confirms`.
 */
export interface CloudTraits {
  /** Every namespace in scope reports real message counts (`supportsMessageCounts`). */
  readonly counts: boolean
  /** Every namespace in scope has a safe, repeatable peek (`supportsRepeatablePeek`) — a trend exists,
   *  and Active can be browsed. Where false, ServiceHub only knows what a person asked it to look at. */
  readonly watched: boolean
  /** The cloud can prove a replayed message stayed out of the dead-letter queue. Read from the
   *  server-computed `FleetCloud.capability` when given (it honours the DLQ-observer attestation);
   *  otherwise falls back to the static `canProveDlqAbsence` preset, which can never be `true` for a
   *  cloud that has since earned the attestation. */
  readonly confirms: boolean
  /** Every namespace in scope supports scheduled/delayed messages. */
  readonly scheduled: boolean
  /** The cloud cannot count messages but does list subscriptions (Google Pub/Sub): the entity tile and
   *  card should read "topics · subscriptions" and list subscriptions, not queues with counts. */
  readonly listsSubscriptionsOnly: boolean
}

/**
 * A capability counts as present only when EVERY namespace in scope has it — a mixed scope reads as
 * the more limited case, never the more capable one. `namespaces` empty (nothing in scope yet) also
 * reads as the limited case, since there is nothing to prove otherwise.
 */
function everyNamespace(namespaces: readonly Namespace[], pick: (c: ProviderCapabilities) => boolean): boolean {
  return namespaces.length > 0 && namespaces.every((n) => n.capabilities !== null && pick(n.capabilities))
}

/**
 * Turns a namespace's own capabilities (never its provider's name) into the traits Home's layout reads.
 * `fleetCloud` is `/fleet/overview`'s entry for this cloud, when one is available — pass it so "confirms"
 * reflects a DLQ-observer attestation, not just the static preset.
 */
export function traitsOf(namespaces: readonly Namespace[], fleetCloud?: FleetCloud): CloudTraits {
  const counts = everyNamespace(namespaces, (c) => c.supportsMessageCounts)
  const subscriptionsOnly = everyNamespace(namespaces, (c) => c.supportsSubscriptions)
  return {
    counts,
    watched: everyNamespace(namespaces, (c) => c.supportsRepeatablePeek),
    confirms: fleetCloud ? fleetCloud.capability === 'canConfirm' : everyNamespace(namespaces, (c) => c.canProveDlqAbsence),
    scheduled: everyNamespace(namespaces, (c) => c.supportsScheduledMessages),
    listsSubscriptionsOnly: !counts && subscriptionsOnly,
  }
}

/** The three shapes a one-cloud view can take (plan §5.3). Layout code switches on this, never on a provider name. */
export type CloudViewKind = 'watched' | 'recordedWithCounts' | 'recordedNoCounts'

/** Which of the three slot layouts a cloud's traits call for. */
export function viewKindOf(traits: CloudTraits): CloudViewKind {
  if (traits.watched) return 'watched'
  return traits.counts ? 'recordedWithCounts' : 'recordedNoCounts'
}
