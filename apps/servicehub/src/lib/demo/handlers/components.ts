import type { ComponentEntity, ComponentOverview, ConnectedComponent, Count, LastLook } from '../../api/components'
import type { Namespace } from '../../api/namespaces'
import { catalogue, rowFor as catalogueRowFor } from '../world/catalogue'
import { activeIn, pendingOf } from '../world/derive'
import type { DemoEntity, World } from '../world/model'
import { notFound, refuse, type Route } from './http'

const UNKNOWN = 'unknown'
const CLOUDS = ['azure', 'aws', 'gcp']
// What one look can record per entity, as the real scanner's cap (5,000). The demo never reaches it.
const LOOK_LIMIT = 5000

const rowFor = (ns: Namespace, kind: string) => catalogueRowFor(ns.provider, kind)

const countOf = (ns: Namespace, value: number): Count =>
  !ns.capabilities!.supportsMessageCounts ? { value: null, kind: 'notReported' } : { value, kind: ns.capabilities!.countsAreApproximate ? 'approximate' : 'exact' }

function build(w: World, now: number): ConnectedComponent[] {
  const work = pendingOf(w, {}).filter((i) => i.kind === 'approval' || i.kind === 'unresolved')
  const out: ConnectedComponent[] = []
  for (const ns of w.namespaces) {
    const watched = ns.capabilities!.supportsRepeatablePeek
    const list: DemoEntity[] = w.entities[ns.id] ?? []
    // A queue another entity names as its dead-letter target is already counted against that entity (as /stats does).
    const targets = new Set(list.map((e) => e.deadLetterTargetName).filter(Boolean))
    const holders = list.filter((e) => e.kind !== 'topic' && !targets.has(e.name))
    const keys = [...new Set(holders.map((e) => rowFor(ns, e.kind)?.componentKey ?? UNKNOWN))]
    for (const key of keys) {
      const row = catalogue.find((r) => r.componentKey === key && r.cloud === ns.provider)
      const mine = holders.filter((e) => (rowFor(ns, e.kind)?.componentKey ?? UNKNOWN) === key)
      const entities: ComponentEntity[] = mine.map((e) => {
        const at = e.name.indexOf('/subscriptions/')
        const recordedHere = w.deadLetters.filter((d) => d.namespaceId === ns.id && d.entityName === e.name && d.status !== 'archived')
        const active = activeIn(w, ns.id, e.name).length
        const recorded: LastLook | null = watched ? null : { messages: recordedHere.length === 0 ? null : active, reachedLimit: active >= LOOK_LIMIT, atUtc: null }
        return {
          name: at > 0 ? e.name.slice(at + '/subscriptions/'.length) : e.name, parent: at > 0 ? e.name.slice(0, at) : null, kind: e.kind,
          deadLetterStore: e.deadLetterTargetName, noStoreReason: null, deadLetters: countOf(ns, active), recorded,
          needsYou: work.filter((i) => i.namespaceId === ns.id && i.entity === e.name).length, observedAtUtc: new Date(now).toISOString(),
        }
      })
      const items = work.filter((i) => i.namespaceId === ns.id && mine.some((e) => e.name === i.entity))
      const kept = entities.filter((e) => e.recorded?.messages != null)
      out.push({
        namespaceId: ns.id, componentKey: key, cloud: ns.provider, name: ns.displayName ?? ns.name, componentName: row?.name ?? 'Other', environment: ns.environment,
        canSend: true, reachable: true, lastError: null, watched, class: row?.class ?? 'r0Unknown',
        deadLetters: countOf(ns, entities.reduce((sum, e) => sum + (e.deadLetters.value ?? 0), 0)),
        recorded: watched ? null : { messages: kept.length === 0 ? null : kept.reduce((sum, e) => sum + e.recorded!.messages!, 0), reachedLimit: entities.some((e) => e.recorded?.reachedLimit), atUtc: null },
        needsYou: items.length, approvals: items.filter((i) => i.kind === 'approval').length, handOffs: items.filter((i) => i.kind === 'unresolved').length,
        observedAtUtc: new Date(now).toISOString(), entities,
      })
    }
  }
  return out.sort((a, b) => CLOUDS.indexOf(a.cloud) - CLOUDS.indexOf(b.cloud) || a.name.localeCompare(b.name) || a.componentKey.localeCompare(b.componentKey))
}

export const components: readonly Route[] = [
  ['get', /^\/fleet\/components$/, (_m, { w, params, now }): ComponentOverview => {
    const cloud = params.cloud
    if (cloud !== undefined && !(typeof cloud === 'string' && CLOUDS.includes(cloud.toLowerCase()))) refuse(400, 'validation_failed', "'cloud' must be azure, aws or gcp.")
    const all = build(w, now)
    return { components: typeof cloud === 'string' ? all.filter((c) => c.cloud === cloud.toLowerCase()) : all, generatedAtUtc: new Date(now).toISOString() }
  }],
  ['get', /^\/fleet\/components\/([^/]+)\/([^/]+)$/, (m, { w, now }): ConnectedComponent => {
    if (!w.namespaces.some((n) => n.id === m[1])) notFound('component')
    return build(w, now).find((c) => c.namespaceId === m[1] && c.componentKey === m[2]) ?? notFound('component')
  }],
]
