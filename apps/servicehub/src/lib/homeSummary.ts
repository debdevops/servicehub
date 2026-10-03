import type { Entity, EntityKind, NamespaceStats } from './api/namespaces'

/**
 * One cloud's numbers, added across ITS namespaces only — Home never mixes clouds (IA §2.3).
 *
 * A total is `null` when any namespace could not supply it. "Cannot know" is a different answer
 * from "none", and the screen renders them differently (R5).
 */
export interface CloudSummary {
  readonly deadLetters: number | null
  readonly active: number | null
  readonly entityCounts: readonly { readonly kind: EntityKind; readonly count: number }[]
  /** Entities holding at least one dead letter. Null when the cloud cannot count per entity. */
  readonly withDeadLetters: number | null
  /** Per kind: how many exist and how many hold dead letters (null when the cloud cannot count per entity). */
  readonly byKind: readonly { readonly kind: EntityKind; readonly count: number; readonly withDeadLetters: number | null }[]
  /** The entities holding the most dead letters, deepest first, at most five. Metadata only — no message is read. */
  readonly needingAttention: readonly Entity[]
  /** Every entity ServiceHub listed, with the namespace it lives in, in the order the cloud gave them. Counts only — no message is read. */
  readonly entities: readonly { readonly namespaceId: string; readonly entity: Entity }[]
}

const sumOrNull = (values: readonly (number | null)[]): number | null =>
  values.some((v) => v === null) ? null : values.reduce<number>((a, b) => a + (b ?? 0), 0)

export function summarise(stats: readonly NamespaceStats[], entities: readonly (readonly Entity[])[]): CloudSummary {
  const byKind = new Map<EntityKind, number>()
  for (const s of stats) for (const e of s.entities) byKind.set(e.kind, (byKind.get(e.kind) ?? 0) + e.count)

  const all = entities.flat()
  const withDl = (e: Entity) => (e.deadLetterMessages ?? 0) > 0
  return {
    byKind: [...byKind]
      .filter(([, count]) => count > 0)
      .map(([kind, count]) => {
        const ofKind = all.filter((e) => e.kind === kind)
        return { kind, count, withDeadLetters: ofKind.some((e) => e.deadLetterMessages === null) ? null : ofKind.filter(withDl).length }
      }),
    entities: entities.flatMap((list, i) => list.map((entity) => ({ namespaceId: stats[i]?.namespaceId ?? '', entity }))),
    needingAttention: all.filter(withDl).sort((a, b) => (b.deadLetterMessages ?? 0) - (a.deadLetterMessages ?? 0)).slice(0, 5),
    deadLetters: sumOrNull(stats.map((s) => s.deadLetterMessages)),
    active: sumOrNull(stats.map((s) => s.activeMessages)),
    entityCounts: [...byKind].map(([kind, count]) => ({ kind, count })).filter((e) => e.count > 0),
    withDeadLetters: all.some((e) => e.deadLetterMessages === null)
      ? null
      : all.filter((e) => (e.deadLetterMessages ?? 0) > 0).length,
  }
}
