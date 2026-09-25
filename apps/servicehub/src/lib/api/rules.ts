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
