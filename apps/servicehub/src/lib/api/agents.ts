import { api } from './client'
import { Intent, withIntent } from './intentHeaders'

export type AgentKind = 'watch' | 'decide' | 'act' | 'maintain'
export type AgentAuthority = 'observes' | 'proposes' | 'actsWithApproval' | 'actsAutonomously'
export type AgentHealth = 'healthy' | 'degraded' | 'failing' | 'paused' | 'unknown'

/** One agent: its own description plus what the running server saw. The screen draws only this — never a per-agent page. */
export interface Agent {
  readonly id: string
  readonly name: string
  readonly purpose: string
  readonly kind: AgentKind
  readonly authority: AgentAuthority
  readonly canAct: boolean
  readonly cadenceSeconds: number
  readonly notes: string | null
  readonly may: readonly string[]
  readonly mayNot: readonly string[]
  /** From its cycles, never self-reported. */
  readonly health: AgentHealth
  /** No cycle for well over its cadence. */
  readonly late: boolean
  readonly isPaused: boolean
  readonly lastRunUtc: string | null
  readonly lastResult: { readonly examined: number; readonly changed: number; readonly summary: string; readonly degraded: boolean } | null
  readonly lastFailure: string | null
  readonly consecutiveFailures: number
}

export interface AgentActivityItem {
  readonly at: string
  /** cycle — seen since the server started · ledger — recorded evidence · audit — a person paused or resumed it. */
  readonly source: 'cycle' | 'ledger' | 'audit'
  readonly kind: string
  readonly text: string
  readonly by: string | null
}

export interface AgentActivity {
  readonly agentId: string
  /** The oldest cycle still remembered; cycles before a restart are not. */
  readonly cyclesSinceUtc: string | null
  readonly items: readonly AgentActivityItem[]
}

export async function fetchAgents(): Promise<Agent[]> {
  return (await api.get<Agent[]>('/agents')).data
}

export async function fetchAgentActivity(id: string): Promise<AgentActivity> {
  return (await api.get<AgentActivity>(`/agents/${encodeURIComponent(id)}/activity`)).data
}

/** Pause removes authority: the agent's loop keeps running and it will not act until resumed. */
export async function pauseAgent(id: string): Promise<Agent> {
  return (await api.post<Agent>(`/agents/${encodeURIComponent(id)}/pause`, undefined, { headers: withIntent(Intent.PauseAgent) })).data
}

/** Resume gives authority back. It is offered on Home (Simple) only — Advanced may take authority away, never give it. */
export async function resumeAgent(id: string): Promise<Agent> {
  return (await api.post<Agent>(`/agents/${encodeURIComponent(id)}/resume`, undefined, { headers: withIntent(Intent.ResumeAgent) })).data
}

/** "every 30 seconds", "every minute", "every hour". */
export function cadenceWords(seconds: number): string {
  if (seconds < 60) return seconds === 1 ? 'every second' : `every ${Math.round(seconds)} seconds`
  if (seconds < 3600) { const m = Math.round(seconds / 60); return m === 1 ? 'every minute' : `every ${m} minutes` }
  const h = Math.round(seconds / 3600)
  return h === 1 ? 'every hour' : `every ${h} hours`
}

/** The authority in the words the design uses. */
export function authorityWords(a: AgentAuthority): string {
  return { observes: 'Observes only', proposes: 'Decides, never acts', actsWithApproval: 'Acts only when a person starts it', actsAutonomously: 'Acts on its own, within limits' }[a]
}
