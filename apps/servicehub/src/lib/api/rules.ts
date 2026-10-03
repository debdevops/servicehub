import { api } from './client'
import type { CloudProvider } from './namespaces'

export interface Rule {
  readonly id: number
  readonly name: string
  readonly provider: CloudProvider
  readonly reason: string | null
  readonly entityName: string | null
  readonly signatureHash: string | null
  readonly maxPerHour: number
  readonly waitSeconds: number
  readonly backOff: boolean
  readonly enabled: boolean
  /** `CircuitBreaker` when it stopped itself, `Person` when someone turned it off. */
  readonly disabledReason: 'CircuitBreaker' | 'Person' | null
  readonly disabledDetail: string | null
  readonly updatedAt: string | null
  /** How many matching messages the safety checks are holding for a person right now. */
  readonly askedCount: number
  /** True when the rule has more matches than one cycle looks at, so `askedCount` is "at least" — show "N+". */
  readonly askedIsLowerBound: boolean
  readonly lastAskedReason: string | null
  /** Counts of what actually happened under the rule — never a stored counter. */
  readonly replayed: number
  readonly lastReplayedAt: string | null
  readonly verifiedOutcomes: number
  readonly stayedFixed: number
  readonly sampleSize: number
  readonly successFloor: number
}

export interface RuleSource {
  readonly signatureHash: string
  readonly reason: string
  readonly entityName: string
  readonly messages: number
  readonly exampleError: string | null
}

export interface RuleTest {
  readonly days: number
  readonly matched: number
  readonly stillWaiting: number
  readonly wouldRun: number
  readonly heldBack: number
  readonly holds: readonly { readonly reasonCode: string; readonly remedy: string; readonly count: number }[]
}

export interface NewRule {
  readonly provider: CloudProvider
  readonly name: string
  readonly reason?: string
  readonly entityName?: string
  readonly signatureHash?: string
  readonly maxPerHour: number
  readonly waitSeconds: number
  readonly backOff: boolean
}

export async function fetchRules(provider: CloudProvider): Promise<Rule[]> {
  return (await api.get<Rule[]>('/rules', { params: { provider } })).data
}

/** The distinct dead letters a cloud's rules are holding for a person — a message two rules match counts once. */
export interface RulesHeld {
  readonly distinct: number
  /** True when a rule has more matches than one cycle looks at, so `distinct` is "at least" — show "N+". */
  readonly isLowerBound: boolean
}

export async function fetchRulesHeld(provider: CloudProvider): Promise<RulesHeld> {
  return (await api.get<RulesHeld>('/rules/held', { params: { provider } })).data
}

export async function fetchRuleSources(provider: CloudProvider): Promise<RuleSource[]> {
  return (await api.get<RuleSource[]>('/rules/sources', { params: { provider } })).data
}

export async function createRule(rule: NewRule): Promise<Rule> {
  return (await api.post<Rule>('/rules', rule)).data
}

export async function setRuleEnabled(id: number, enabled: boolean): Promise<Rule> {
  return (await api.post<Rule>(`/rules/${id}/enabled`, { enabled })).data
}

export async function testRule(rule: Pick<NewRule, 'provider' | 'reason' | 'entityName' | 'signatureHash'>): Promise<RuleTest> {
  return (await api.post<RuleTest>('/rules/test', { ...rule, days: 7 })).data
}

/** Name and pace only — what a rule matches never changes; a different failure is a new rule. */
export async function updateRule(id: number, rule: Pick<NewRule, 'name' | 'maxPerHour' | 'waitSeconds' | 'backOff'>): Promise<Rule> {
  return (await api.put<Rule>(`/rules/${id}`, rule)).data
}

/** Deletes the rule. What it replayed stays in the ledger and the replay history. */
export async function deleteRule(id: number): Promise<void> {
  await api.delete(`/rules/${id}`)
}

/** The dead letters the rule matches right now and that are still waiting. Sends nothing. */
export async function fetchRuleMatches(id: number): Promise<number[]> {
  return (await api.get<number[]>(`/rules/${id}/matches`, { params: { limit: 500 } })).data
}

/** Makes rules for the most common failures no rule covers yet. Returns the rules it made. */
export async function generateRules(provider: CloudProvider, max = 5): Promise<Rule[]> {
  return (await api.post<Rule[]>('/rules/generate', { provider, max })).data
}
