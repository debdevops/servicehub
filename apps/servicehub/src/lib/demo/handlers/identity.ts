import type { AuditPage, Me } from '../../api/identity'
import { inScope, scopeOf } from '../world/derive'
import { people } from '../world/seed'
import { page, type Route } from './http'

export const identity: readonly Route[] = [
  ['get', /^\/me$/, (_m, { w }): Me => ({
    ownerId: 'demo', authMethod: 'demo', actor: people.you, effectiveRole: 'Admin', governanceActive: true, grantors: [people.maya.label], recoverRole: 'Admin',
    namespaceRecoverRoles: Object.fromEntries(w.namespaces.map((n) => [n.id, 'Admin' as const])),
  })],
  ['get', /^\/audit$/, (_m, { w, params }): AuditPage => {
    const scope = scopeOf(params)
    const scoped = scope.provider || scope.namespaceId || scope.environment
    const rows = w.audit.filter((a) => (!params.action || a.action === params.action) && (!scoped || (a.namespaceId !== null && inScope(w, a.namespaceId, scope))))
    return page(rows, params, 10)
  }],
]
