import { describe, expect, it } from 'vitest'
import { helpAnswers } from '../content/help'
import { navigation } from '../nav/navigation'
import type { Entity, Namespace } from './api/namespaces'
import { search } from './searchIndex'

const ns = (id: string, provider: Namespace['provider'], name: string) =>
  ({ id, name, displayName: null, provider, environment: 'Development' }) as unknown as Namespace
const q = (name: string, dead: number | null): Entity => ({ name, kind: 'queue', activeMessages: 0, deadLetterMessages: dead, deadLetterTargetName: null })

const namespaces = [ns('a', 'azure', 'orders-bus'), ns('w', 'aws', 'payments')]
const entities = new Map([['a', [q('orders', 3), { ...q('orders-topic', null), kind: 'topic' as const }]], ['w', [q('orders-dlq', null)]]])

describe('search (⌘K)', () => {
  it('finds queues in every cloud with where they live, and links to their dead letters on the right cloud', () => {
    const r = search('orders', namespaces, entities, [], []).filter((x) => x.group === 'Queues and topics')
    expect(r.map((x) => x.title)).toEqual(['orders', 'orders-dlq'])
    expect(r[0].detail).toContain('3 dead-lettered')
    expect(r[0].href).toBe('/?tab=dlq&ns=a&entity=orders')
    expect(r[1].provider).toBe('aws')
  })

  it('finds places from the navigation array and help answers; every word must match', () => {
    expect(search('ledger', [], new Map(), navigation, []).some((x) => x.group === 'Do something')).toBe(true)
    expect(search('replay many', [], new Map(), [], helpAnswers).map((x) => x.title)).toContain('Replay many at once')
    expect(search('replay zebra', [], new Map(), [], helpAnswers)).toEqual([])
  })

  it('returns nothing for an empty query', () => {
    expect(search('  ', namespaces, entities, navigation, helpAnswers)).toEqual([])
  })
})
