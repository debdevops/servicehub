import type { Signature } from './api/signatures'

/** The words for how replaying has gone, from the ledger — never a guess, and never "verified" where nothing was verified. */
export function replayWords(s: Signature): string {
  const r = s.replays
  const verified = r.stayedFixed + r.returned
  if (r.replayed === 0) return 'not replayed'
  if (verified === 0) return `${r.replayed} replayed, none verified`
  return `${r.stayedFixed} of ${verified} stayed fixed`
}
