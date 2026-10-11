import type { FleetFailure, FleetOverview, FleetTopFailure } from '../../api/fleet'
import type { CloudProvider } from '../../api/namespaces'
import { DAY, proves } from '../world/derive'
import type { DemoDeadLetter } from '../world/model'
import type { Route } from './http'

const top = (rows: DemoDeadLetter[]): FleetFailure[] => {
  const counts = new Map<string, number>()
  rows.forEach((d) => d.deadLetterReason && counts.set(d.deadLetterReason, (counts.get(d.deadLetterReason) ?? 0) + 1))
  return [...counts].map(([reason, count]) => ({ reason, count })).sort((a, b) => b.count - a.count)
}

export const fleet: readonly Route[] = [
  ['get', /^\/fleet\/overview$/, (_m, { w, params, now }): FleetOverview => {
    const window = (params.window as FleetOverview['window']) ?? '24h'
    const since = now - (window === '7d' ? 7 : 1) * DAY
    const active = (ids: string[]) => w.deadLetters.filter((d) => ids.includes(d.namespaceId) && d.status === 'active')
    const arrived = (ids: string[]) => w.deadLetters.filter((d) => ids.includes(d.namespaceId) && Date.parse(d.detectedAtUtc) >= since).length
    const left = (ids: string[]) => w.deadLetters.filter((d) => ids.includes(d.namespaceId) && d.resolvedAt && Date.parse(d.resolvedAt) >= since).length
    const providers = [...new Set(w.namespaces.map((n) => n.provider))] as CloudProvider[]
    const topFailures: FleetTopFailure[] = w.namespaces.flatMap((n) => top(active([n.id])).map((f) => ({ provider: n.provider, environment: n.environment, ...f })))
    return {
      window, since: new Date(since).toISOString(),
      clouds: providers.map((provider) => {
        const ids = w.namespaces.filter((n) => n.provider === provider).map((n) => n.id)
        return { provider, namespaceCount: ids.length, capability: proves(provider) ? 'canConfirm' : 'observerRequired', watched: w.namespaces.find((n) => n.provider === provider)!.capabilities!.supportsRepeatablePeek, active: active(ids).length, newInWindow: arrived(ids), resolvedInWindow: left(ids) }
      }),
      namespaces: w.namespaces.map((n) => {
        const stuck = active([n.id])
        const watched = n.capabilities!.supportsRepeatablePeek
        return { id: n.id, name: n.name, displayName: n.displayName, provider: n.provider, environment: n.environment, watched, active: stuck.length, newInWindow: arrived([n.id]), resolvedInWindow: left([n.id]), topFailure: top(stuck)[0] ?? null, health: !watched ? 'cannotTell' : stuck.length > 0 ? 'needsALook' : 'healthy' }
      }),
      topFailures: topFailures.sort((a, b) => b.count - a.count).slice(0, 5),
    }
  }],
]
