import { describe, expect, it } from 'vitest'
import type { Me } from './api/identity'
import { permission } from './permissions'

const me = (over: Partial<Me>): Me => ({ ownerId: 'o', authMethod: 'ApiKey', actor: { identity: 'ApiKey:reader', kind: 'apiKey', label: 'ApiKey:reader', isSession: false }, effectiveRole: 'Viewer', governanceActive: true, grantors: ['ApiKey:lead'], ...over }) as Me

describe('permission', () => {
  it('allows everything while governance is off — the server still enforces', () => expect(permission(me({ governanceActive: false }), 'Admin', 'x').allowed).toBe(true))
  it('says what is missing and who can grant it', () => {
    const p = permission(me({}), 'Operator', 'replay this message', { recover: true })
    expect(p.allowed).toBe(false)
    expect(p.reason).toBe('To replay this message you need the Operator role. You have no role here. ApiKey:lead can grant it.')
  })
  it('lets a namespace grant add rights the fleet role lacks', () => {
    const m = me({ recoverRole: 'Viewer', namespaceRecoverRoles: { n1: 'Operator' } })
    expect(permission(m, 'Operator', 'replay', { recover: true, namespaceId: 'n1' }).allowed).toBe(true)
    expect(permission(m, 'Operator', 'replay', { recover: true, namespaceId: 'n2' }).allowed).toBe(false)
  })
  it('allows while /me is still loading rather than disabling on a guess', () => expect(permission(undefined, 'Admin', 'x').allowed).toBe(true))
})
