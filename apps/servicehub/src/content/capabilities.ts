import type { CloudProvider } from '../lib/api/namespaces'
import type { CloudTraits } from '../lib/home/traits'

/** One line in the capability line — ✓ where the cloud can, — where it can't (never a raw `true`/`false`). */
export interface CapabilityWord {
  readonly ok: boolean
  readonly text: string
}

/**
 * The words behind Home's capability line and every "can't count" / "not watched" cell (plan §3, §5.3).
 * One source, so the compact line (all-clouds cards) and the full line (a cloud's own header) never
 * disagree about what a trait means in words. `full` adds the one word (scheduled messages) the compact
 * line drops for space.
 */
export function capabilityWords(traits: CloudTraits, full = false): readonly CapabilityWord[] {
  const words: CapabilityWord[] = [
    { ok: traits.counts, text: traits.counts ? 'Counts messages' : 'No message counts' },
    { ok: traits.watched, text: traits.watched ? 'Watched automatically' : 'Recorded when you look' },
    {
      ok: traits.watched,
      text: traits.watched ? 'Browse active safely' : traits.counts ? 'Counts only, no browsing' : 'No browsing',
    },
    { ok: traits.confirms, text: traits.confirms ? 'Confirms a fix held' : 'Can’t confirm fixes yet' },
  ]
  if (full) words.push({ ok: traits.scheduled, text: traits.scheduled ? 'Scheduled messages' : 'No scheduled messages' })
  return words
}

/**
 * Why a cloud that isn't watched can't be watched, in one sentence — presentation copy, not a layout
 * decision (R4 governs which CARDS appear, not what a sentence about a named cloud may say). Azure is
 * never shown this bar at all, since it is always watched.
 */
export const lookNowReason: Readonly<Record<CloudProvider, string>> = {
  azure: '',
  aws: 'SQS has no way to look without receiving each message, which counts toward its own retry limit.',
  gcp: 'Pub/Sub counts every pull as a delivery attempt, so watching repeatedly could dead-letter a message by accident.',
}

/** Why a cloud that can't count messages can't count them — used by the GCP-shaped tiles. */
export const noCountsReason: Readonly<Record<CloudProvider, string>> = {
  azure: '',
  aws: '',
  gcp: 'Pub/Sub has no count API, so ServiceHub can’t report a live number here.',
}
