import type { Agent, AgentActivity, AgentActivityItem } from '../../api/agents'
import { audit } from '../world/actions'
import type { World } from '../world/model'
import { people } from '../world/seed'
import { emit, save } from '../world/store'
import { notFound, type Route } from './http'

const view = (a: Agent): Agent => ({ ...a, health: a.isPaused ? 'paused' : a.health })
const find = (w: World, id: string): Agent => w.agents.find((a) => a.id === decodeURIComponent(id)) ?? notFound('agent')

/** What an agent did, read back from the ledger and the audit trail — plus the cycles it has run while the demo was open. */
function activity(w: World, agent: Agent): AgentActivityItem[] {
  const items: AgentActivityItem[] = [...(w.activity[agent.id] ?? [])]
  if (agent.id === 'auto-replay') {
    for (const e of w.entries.filter((x) => x.actor.kind === 'automation').slice(-40)) items.push({ at: e.begunAt, source: 'ledger', kind: 'replayed', text: `Replayed a message on ${e.entityName} for a rule`, by: null })
  }
  if (agent.id === 'recovery-verification') {
    for (const e of w.entries.filter((x) => x.kind === 'Replay' && x.closedAt).slice(-40)) {
      items.push({ at: e.closedAt!, source: 'ledger', kind: e.state, text: e.state === 'Recovered' ? `Confirmed a replay on ${e.entityName} stayed fixed` : e.state === 'Returned' ? `Saw a replayed message come back on ${e.entityName}` : `Closed a watch on ${e.entityName}: this cloud cannot prove it`, by: null })
    }
  }
  for (const a of w.audit.filter((x) => (x.action === 'Agent.Pause' || x.action === 'Agent.Resume') && x.resourceName === agent.name)) {
    items.push({ at: a.timestamp, source: 'audit', kind: a.action, text: a.action === 'Agent.Pause' ? 'Paused' : 'Resumed', by: a.actor.label })
  }
  return items.sort((a, b) => b.at.localeCompare(a.at)).slice(0, 60)
}

function setPaused(w: World, agent: Agent, paused: boolean, now: number): Agent {
  w.agents = w.agents.map((a) => (a.id === agent.id ? { ...a, isPaused: paused } : a))
  audit(w, people.you, paused ? 'Agent.Pause' : 'Agent.Resume', null, agent.name, now)
  save()
  emit(paused ? 'AgentPaused' : 'AgentResumed', 'Agent', null)
  return view({ ...agent, isPaused: paused })
}

export const agents: readonly Route[] = [
  ['get', /^\/agents$/, (_m, { w }) => w.agents.map(view)],
  ['get', /^\/agents\/dormant$/, () => []],
  ['get', /^\/agents\/([^/]+)\/activity$/, (m, { w }): AgentActivity => { const agent = find(w, m[1]); return { agentId: agent.id, cyclesSinceUtc: new Date(w.builtAt).toISOString(), items: activity(w, agent) } }],
  ['post', /^\/agents\/([^/]+)\/pause$/, (m, { w, now }) => setPaused(w, find(w, m[1]), true, now)],
  ['post', /^\/agents\/([^/]+)\/resume$/, (m, { w, now }) => setPaused(w, find(w, m[1]), false, now)],
]
