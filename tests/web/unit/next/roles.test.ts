import { describe, expect, it } from 'vitest'
import { abilities, grantScope, granteeWords, roleRows } from '@/next/lib/roles'

describe('the role table', () => {
  const rank = ['Viewer', 'Operator', 'Approver', 'Admin']
  const has = (v: boolean | string) => v !== false

  it('lists the four roles from least to most, as the server ranks them', () => {
    expect(roleRows.map((r) => r.role)).toEqual(rank)
  })

  it('a higher role never loses an ability a lower one has: the property the server’s ranking guarantees', () => {
    for (let i = 1; i < roleRows.length; i++) {
      for (const a of abilities) {
        if (has(roleRows[i - 1].can[a.id])) expect(has(roleRows[i].can[a.id]), `${roleRows[i].role} ${a.id}`).toBe(true)
      }
    }
  })

  it('reading is open to every role, and only an Admin manages access', () => {
    for (const r of roleRows) expect(r.can.read, r.role).toBe(true)
    expect(roleRows.filter((r) => r.can.access).map((r) => r.role)).toEqual(['Admin'])
  })

  it('an Operator may replay but not approve, and may only switch a rule off or delete it', () => {
    const op = roleRows.find((r) => r.role === 'Operator')!
    expect(op.can.replay).toBe(true)
    expect(op.can.approve).toBe(false)
    expect(op.can.rules).toBe('Switch off or delete only')
  })

  it('an Approver may approve and change rules', () => {
    const ap = roleRows.find((r) => r.role === 'Approver')!
    expect([ap.can.approve, ap.can.rules]).toEqual([true, true])
  })
})

describe('grant words', () => {
  it('names the grantee kind and what a grant covers', () => {
    expect(granteeWords('User')).toBe('Person')
    expect(granteeWords('ApiKey')).toBe('API key')
    expect(grantScope({ namespaceId: null, pillarKind: null })).toBe('Everything')
    expect(grantScope({ namespaceId: 'n1', pillarKind: null })).toBe('This connection')
    expect(grantScope({ namespaceId: null, pillarKind: 'Recover' })).toBe('Everything, recover only')
  })
})
