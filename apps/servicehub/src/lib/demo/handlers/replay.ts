import type { ReplayPage } from '../../api/replay'
import { inScope, newestFirst, replaysOf, scopeOf, toReplay, withinWindow } from '../world/derive'
import type { DemoEntry } from '../world/model'
import { page, type Route } from './http'

const endings: Readonly<Record<string, DemoEntry['state']>> = { fixed: 'Recovered', watching: 'Observing', returned: 'Returned', unproven: 'Unverified', notsent: 'ExecutionFailed' }

export const byWhom = (e: DemoEntry, by: unknown) => !by || (by === 'autonomous' ? e.actor.kind === 'automation' : e.actor.kind !== 'automation')

export const replays: readonly Route[] = [
  ['get', /^\/replays$/, (_m, { w, params: p, now }): ReplayPage => {
    const scope = scopeOf(p)
    const q = p.q ? String(p.q).toLowerCase() : null
    const rows = newestFirst(replaysOf(w).filter((e) =>
      inScope(w, e.namespaceId, scope)
      && (p.dlqMessageId === undefined || e.dlqMessageId === Number(p.dlqMessageId))
      && (p.ruleId === undefined || e.ruleId === Number(p.ruleId))
      && (!p.result || (p.result === 'accepted') === (e.state !== 'ExecutionFailed'))
      && (!p.ending || e.state === endings[String(p.ending)])
      && byWhom(e, p.by)
      && (!p.entity || e.entityName === p.entity)
      && (!q || `${e.messageId} ${e.entityName}`.toLowerCase().includes(q))
      && withinWindow(e.begunAt, p.window, now)))
    return page(rows.map((e) => toReplay(w, e)), p)
  }],
]
