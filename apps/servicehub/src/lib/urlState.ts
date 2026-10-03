import { NO_REASON } from '../components/message/FailureGroups'

/**
 * Side drawers that ride in the address: a dead letter (`message=`, `view=`), an active message (`active=`) and a row's own Replay
 * (`replay=`). Anything that opens a DIFFERENT window from a link — Settings, Help, Add a cloud, Approve, Send — puts them away
 * first, so one click opens one thing and never a window stacked over a forgotten drawer.
 */
export const DRAWER_PARAMS = ['message', 'view', 'active', 'replay'] as const

/** The dead letters a rule is holding for a person: its cloud, its failure reason and its queue, on the Dead letters tab. */
/**
 * A reason as the Dead letters tab's address wants it. "Unknown" is what every screen and every rule calls a message whose cloud recorded
 * no reason (Google Cloud, often) — the list has no reason called that, only the "no reason" filter, so "Unknown" must ask for that.
 */
export function reasonParam(reason: string): string {
  return reason === 'Unknown' ? NO_REASON : reason
}

export function waitingHref(rule: { provider: string; reason: string | null; entityName: string | null }): string {
  const p = new URLSearchParams({ tab: 'dlq', provider: rule.provider })
  if (rule.reason) p.set('reason', reasonParam(rule.reason))
  if (rule.entityName) p.set('entity', rule.entityName)
  return `/?${p.toString()}`
}

export function withoutDrawers(params: URLSearchParams): URLSearchParams {
  const next = new URLSearchParams(params)
  DRAWER_PARAMS.forEach((k) => next.delete(k))
  return next
}
