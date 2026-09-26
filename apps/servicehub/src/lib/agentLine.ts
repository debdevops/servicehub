import type { Agent } from './api/agents'
import type { Namespace } from './api/namespaces'

export interface AgentLine {
  /** WATCHING, or WILL NOT ACT while every acting agent is paused. */
  readonly status: 'watching' | 'will-not-act' | 'partly-paused'
  readonly text: string
}

const plural = (n: number, one: string, many = `${one}s`) => `${n.toLocaleString()} ${n === 1 ? one : many}`

/**
 * The agent bar's one line about the selected cloud (unit 4.6). Every clause is computed from the namespaces'
 * capabilities and the running agents — never from a cloud's name — so "Azure may act on its own" is only ever said
 * where the namespace can actually prove a fix held (R4).
 */
export function agentLine(cloud: string, namespaces: readonly Namespace[], queues: number | null, agents: readonly Agent[]): AgentLine {
  const acting = agents.filter((a) => a.canAct)
  const paused = acting.filter((a) => a.isPaused)
  const status: AgentLine['status'] = acting.length > 0 && paused.length === acting.length ? 'will-not-act' : paused.length > 0 ? 'partly-paused' : 'watching'

  const watched = namespaces.length > 0 && namespaces.every((n) => n.capabilities?.supportsRepeatablePeek === true)
  const watching = watched
    ? queues === null ? `Watching ${cloud}` : `Watching ${plural(queues, 'queue')} and subscriptions`
    : `Not watching ${cloud} on its own — ask it to look from Dead letters`

  if (status === 'will-not-act') return { status, text: `${watching} · Paused: the Agent will not act here until someone resumes it` }

  const proving = namespaces.filter((n) => n.capabilities?.canProveDlqAbsence === true).length
  const acts =
    proving === namespaces.length && proving > 0
      ? `${cloud} can prove a fix held, so the Agent may act here on its own once a failure has earned it`
      : proving === 0
        ? `${cloud} can’t prove a fix held, so the Agent asks you before every replay`
        : `${proving} of ${namespaces.length} namespaces can prove a fix held; the rest ask you before every replay`
  const partly = status === 'partly-paused' ? ` · ${paused.map((a) => a.name).join(', ')} paused` : ''
  return { status, text: `${watching} · ${acts}${partly}` }
}
