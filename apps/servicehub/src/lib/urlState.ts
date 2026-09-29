/**
 * Side drawers that ride in the address: a dead letter (`message=`, `view=`), an active message (`active=`) and a row's own Replay
 * (`replay=`). Anything that opens a DIFFERENT window from a link — Settings, Help, Add a cloud, Approve, Send — puts them away
 * first, so one click opens one thing and never a window stacked over a forgotten drawer.
 */
export const DRAWER_PARAMS = ['message', 'view', 'active', 'replay'] as const

/** The dead letters a rule is holding for a person: its cloud, its failure reason and its queue, on the Dead letters tab. */
export function waitingHref(rule: { provider: string; reason: string | null; entityName: string | null }): string {
  const p = new URLSearchParams({ tab: 'dlq', provider: rule.provider })
  if (rule.reason) p.set('reason', rule.reason)
  if (rule.entityName) p.set('entity', rule.entityName)
  return `/?${p.toString()}`
}

export function withoutDrawers(params: URLSearchParams): URLSearchParams {
  const next = new URLSearchParams(params)
  DRAWER_PARAMS.forEach((k) => next.delete(k))
  return next
}
