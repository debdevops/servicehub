import { describe, expect, it } from 'vitest'
import type { Entity, NamespaceStats } from './api/namespaces'
import { summarise } from './homeSummary'

const stats = (over: Partial<NamespaceStats>): NamespaceStats => ({
  namespaceId: 'n', entities: [], activeMessages: 0, deadLetterMessages: 0, messageCountsSupported: true, observedAt: 'now', ...over,
})
const entity = (deadLetterMessages: number | null): Entity =>
  ({ name: 'q', kind: 'queue', activeMessages: 0, deadLetterMessages, deadLetterTargetName: null })

describe('summarise', () => {
  it('adds a cloud’s namespaces together', () => {
    const s = summarise(
      [stats({ deadLetterMessages: 3, activeMessages: 10, entities: [{ kind: 'queue', count: 2 }] }),
       stats({ deadLetterMessages: 4, activeMessages: 5, entities: [{ kind: 'queue', count: 1 }, { kind: 'topic', count: 1 }] })],
      [[entity(1), entity(0)], [entity(5)]],
    )
    expect(s).toMatchObject({
      deadLetters: 7, active: 15, withDeadLetters: 2,
      entityCounts: [{ kind: 'queue', count: 3 }, { kind: 'topic', count: 1 }],
      byKind: [{ kind: 'queue', count: 3, withDeadLetters: 2 }, { kind: 'topic', count: 1, withDeadLetters: 0 }],
    })
    expect(s.needingAttention.map((e) => e.deadLetterMessages)).toEqual([5, 1])
  })

  it('ranks queues by dead letters, caps at five, and leaves out empty ones', () => {
    const many = [0, 3, 9, 1, 4, 2, 7].map(entity)
    const s = summarise([stats({ entities: [{ kind: 'queue', count: 7 }] })], [many])
    expect(s.needingAttention.map((e) => e.deadLetterMessages)).toEqual([9, 7, 4, 3, 2])
  })

  it('does not call a cloud healthy when it cannot count per entity', () => {
    const s = summarise([stats({ entities: [{ kind: 'queue', count: 1 }] })], [[entity(null)]])
    expect(s.byKind[0].withDeadLetters).toBeNull()
    expect(s.needingAttention).toEqual([])
  })

  it('keeps zero as zero', () => {
    expect(summarise([stats({})], [[entity(0)]])).toMatchObject({ deadLetters: 0, active: 0, withDeadLetters: 0 })
  })

  it('says unknown, not zero, when a cloud cannot count', () => {
    const s = summarise([stats({ deadLetterMessages: null, activeMessages: null, messageCountsSupported: false })], [[entity(null)]])
    expect(s).toMatchObject({ deadLetters: null, active: null, withDeadLetters: null })
  })

  it('is unknown if any one namespace cannot supply it', () => {
    expect(summarise([stats({ deadLetterMessages: 3 }), stats({ deadLetterMessages: null })], []).deadLetters).toBeNull()
  })
})
