import type { SignatureTrust } from './api/signatures'

const pct = (r: number) => `${Math.round(r * 100)}%`

/**
 * One plain answer to "can ServiceHub replay this failure on its own?" — from the trust report, never a guess. The honest
 * default is no: a person approving each replay is a floor most failures should stay on.
 */
export function trustWords(t: SignatureTrust, cloud: string): { answer: 'yes' | 'no' | 'not yet'; text: string } {
  const soFar = t.sampleSize === 0
    ? 'Nothing has been replayed and verified yet.'
    : `So far ${t.recovered} of ${t.sampleSize} verified replays stayed fixed (${pct(t.verifiedSuccessRate ?? 0)}).`

  if (t.level === 'unattended') return { answer: 'yes', text: `It has earned replaying on its own — 30 or more verified fixes at 99% or better. ${soFar}` }
  if (t.level === 'standing') {
    const next = t.nextLevel && t.moreVerifiedNeeded !== null && t.rateNeeded !== null
      ? ` The next step needs ${t.moreVerifiedNeeded} more at ${pct(t.rateNeeded)}.`
      : ''
    return { answer: 'yes', text: `Auto-replay rules may replay it without asking — it earned 10 or more verified fixes at 95% or better. ${soFar}${next}` }
  }
  if (t.productionCeiling) return { answer: 'no', text: `This is Production: a person approves every replay there, always. ${soFar}` }
  if (!t.cloudCanConfirm) {
    return { answer: 'no', text: `${cloud} can’t confirm that a replayed message stayed out of the dead-letter queue, so a person approves every replay here. ${soFar}` }
  }
  const need = t.moreVerifiedNeeded ?? 0
  const rate = t.rateNeeded !== null ? pct(t.rateNeeded) : '95%'
  const text = need > 0
    ? `A person approves each replay until it has ${need} more verified ${need === 1 ? 'fix' : 'fixes'}, at ${rate} or better. ${soFar}`
    : `It has enough verified replays, but not at ${rate} or better, so a person still approves each one. ${soFar}`
  return { answer: 'not yet', text }
}
