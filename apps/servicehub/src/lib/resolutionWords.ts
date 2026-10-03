import type { DeadLetter, ResolutionCause } from './api/deadLetters'

/** How it left, in words that claim no more than was recorded: absence proves it is gone, not who removed it (R5). */
export const causeWords: Readonly<Record<ResolutionCause, string>> = {
  replayedByServiceHub: 'Replayed by ServiceHub',
  purgedByServiceHub: 'Purged by ServiceHub',
  vanishedExternally: 'Left the queue — ServiceHub did not see how',
  declaredByOperator: 'Marked handled by a person',
  unknown: 'How it left was not recorded',
}

/**
 * The single reading of "how it left" for a resolved row — the same words wherever a resolved
 * message is shown (the list's Now column and the message drawer, unit 6.11). A recorded
 * `resolutionCause` always wins; older rows recorded before that column existed fall back to
 * what `status` alone can honestly say.
 */
export function resolutionWords(row: Pick<DeadLetter, 'resolutionCause' | 'status'>): string {
  if (row.resolutionCause) return causeWords[row.resolutionCause]
  if (row.status === 'replayed') return causeWords.replayedByServiceHub
  if (row.status === 'discarded') return 'Discarded on purpose'
  if (row.status === 'replayFailed') return 'A replay was tried and failed'
  return causeWords.unknown
}
