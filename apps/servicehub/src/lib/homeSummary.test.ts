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
    expect(s).toEqual({
      deadLetters: 7, active: 15, withDeadLetters: 2,
      entityCounts: [{ kind: 'queue', count: 3 }, { kind: 'topic', count: 1 }],
    })
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
