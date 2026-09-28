import type { Rule, RulesHeld } from './api/rules'

/** "4", or "4+" when the rule has more matches than a cycle looks at — a count that is a floor must never read as exact. */
export function heldWords(rules: readonly Pick<Rule, 'askedCount' | 'askedIsLowerBound'>[]): string {
  const total = rules.reduce((n, r) => n + r.askedCount, 0)
  return `${total.toLocaleString()}${rules.some((r) => r.askedIsLowerBound) ? '+' : ''}`
}

/** The distinct messages held across ALL of a cloud's rules ("2,075" would be the per-rule sum, which counts an overlapping message once per rule). */
export function distinctHeldWords(held: RulesHeld): string {
  return `${held.distinct.toLocaleString()}${held.isLowerBound ? '+' : ''}`
}
