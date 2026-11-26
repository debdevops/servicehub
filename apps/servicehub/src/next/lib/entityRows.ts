import type { ComponentEntity, Count } from '../../lib/api/components'

export type EntityRow =
  | { readonly kind: 'topic'; readonly key: string; readonly name: string }
  | { readonly kind: 'place'; readonly key: string; readonly entity: ComponentEntity; readonly active: Count | null }

/** The full path an entity has in the entities endpoint, which is how the two reads are joined. */
export const fullName = (e: Pick<ComponentEntity, 'name' | 'parent'>) => (e.parent ? `${e.parent}/subscriptions/${e.name}` : e.name)

/**
 * Lays the places out as the cloud does: a topic heading with its subscriptions beneath it, queues on their own. A topic holds
 * nothing itself, so its row carries a sentence instead of counts (it never shows a 0 that looks like a measurement).
 */
export function entityRows(entities: readonly ComponentEntity[], activeOf: (fullPath: string) => Count | null): EntityRow[] {
  const place = (e: ComponentEntity): EntityRow => ({ kind: 'place', key: fullName(e), entity: e, active: activeOf(fullName(e)) })
  const byName = (a: ComponentEntity, b: ComponentEntity) => a.name.localeCompare(b.name)
  const topics = new Map<string, ComponentEntity[]>()
  for (const e of entities.filter((x) => x.parent !== null)) topics.set(e.parent as string, [...(topics.get(e.parent as string) ?? []), e])
  // Topics first, each with its subscriptions beneath it; then queues on their own — the order a cloud's own console shows.
  return [
    ...[...topics.keys()].sort((a, b) => a.localeCompare(b)).flatMap((topic): EntityRow[] => [
      { kind: 'topic', key: `topic:${topic}`, name: topic },
      ...(topics.get(topic) ?? []).sort(byName).map(place),
    ]),
    ...entities.filter((x) => x.parent === null).sort(byName).map(place),
  ]
}


/** A subscription is stored as its full path (`topic/subscriptions/name`); show it as `topic / name`, never the topic twice. */
export function entityLabel(entityName: string, topicName: string | null): string {
  const at = entityName.indexOf('/subscriptions/')
  if (at > 0) return `${entityName.slice(0, at)} / ${entityName.slice(at + '/subscriptions/'.length)}`
  return topicName ? `${topicName} / ${entityName}` : entityName
}
