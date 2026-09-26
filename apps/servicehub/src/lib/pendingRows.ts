import type { PendingWorkItem } from './api/pendingWork'
import { providerLabel } from './providers'

/** One row the bell, the strip and the Waiting tab draw — several approvals in one namespace are one question. */
export interface PendingRow {
  readonly key: string
  readonly kind: PendingWorkItem['kind']
  readonly title: string
  /** Where: cloud · queue(s). */
  readonly where: string
  /** Why it stopped, in plain words — the server's sentence for the reason code. */
  readonly why: string
  readonly reasonCode: string
  readonly count: number
  readonly since: string
  readonly action: { readonly label: string; readonly href: string }
  readonly items: readonly PendingWorkItem[]
}

const plural = (n: number, one: string, many = `${one}s`) => `${n} ${n === 1 ? one : many}`

/**
 * Turns pending items into rows, most urgent first (the server's order): a stopped agent, a stopped rule, then questions.
 * Approvals in the same namespace become one row — "2 replays need your approval" — with one Review action; anything
 * else is one row each. The action opens the flow that resolves it; it never marks anything read.
 */
export function pendingRows(items: readonly PendingWorkItem[]): PendingRow[] {
  const rows: PendingRow[] = []
  const groups = new Map<string, PendingWorkItem[]>()
  for (const i of items) {
    if (i.kind === 'approval') {
      const key = `${i.provider ?? '?'}:${i.namespaceId ?? '?'}`
      const g = groups.get(key)
      if (g) {
        g.push(i)
        continue
      }
      groups.set(key, [i])
      rows.push({ key, kind: 'approval', title: '', where: '', why: '', reasonCode: i.reasonCode, count: 0, since: i.since, action: { label: 'Review', href: '' }, items: [] })
      continue
    }

    const cloud = i.provider ? providerLabel[i.provider] : null
    rows.push(i.kind === 'rule'
      ? {
          key: i.id, kind: 'rule', title: 'A rule stopped itself', where: [cloud, i.ruleName].filter(Boolean).join(' · '), why: i.reason,
          reasonCode: i.reasonCode, count: 1, since: i.since, action: { label: 'Look at the rule', href: `?panel=rules&rule=${i.ruleId}` }, items: [i],
        }
      : {
          key: i.id, kind: 'agent', title: 'An agent stopped working', where: 'ServiceHub', why: i.reason,
          reasonCode: i.reasonCode, count: 1, since: i.since, action: { label: 'Look', href: `/advanced/agents?agent=${i.agentId}` }, items: [i],
        })
  }

  return rows.map((r) => {
    if (r.kind !== 'approval') return r
    const g = groups.get(r.key)!
    const first = g[0]
    const entities = [...new Set(g.map((i) => i.entity).filter(Boolean))]
    const cloud = first.provider ? providerLabel[first.provider] : 'A cloud'
    return {
      ...r,
      title: `${plural(g.length, 'replay')} ${g.length === 1 ? 'needs' : 'need'} your approval`,
      where: [cloud, first.namespaceName, entities.slice(0, 3).join(', ') + (entities.length > 3 ? ` +${entities.length - 3}` : '')].filter(Boolean).join(' · '),
      why: first.reason,
      count: g.length,
      action: { label: 'Review', href: `?modal=approve&group=${encodeURIComponent(r.key)}` },
      items: g,
    }
  })
}
