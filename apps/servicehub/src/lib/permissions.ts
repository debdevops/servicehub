import type { Me, Role } from './api/identity'

const rank: Readonly<Record<Role, number>> = { Viewer: 0, Operator: 1, Approver: 2, Admin: 3 }

export interface Permission {
  readonly allowed: boolean
  /** Why not, and who can grant it — shown beside the disabled action, never hidden. Null when allowed. */
  readonly reason: string | null
}

/**
 * Whether the caller may do something (unit 5.7), from `/me` — the same evaluator the server uses. Unknown (`/me` not loaded,
 * or no governance) is ALLOWED here: the server still enforces every action, and a control disabled on a guess would be worse
 * than a refusal that explains itself. `recover` actions read the recovery role, per namespace where one is given, because a
 * namespace grant can add rights the fleet role lacks.
 */
export function permission(me: Me | undefined, needed: Role, what: string, opts: { recover?: boolean; namespaceId?: string } = {}): Permission {
  if (!me || !me.governanceActive) return { allowed: true, reason: null }
  const role: Role | null | undefined = opts.recover
    ? opts.namespaceId && me.namespaceRecoverRoles && opts.namespaceId in me.namespaceRecoverRoles
      ? me.namespaceRecoverRoles[opts.namespaceId]
      : me.recoverRole
    : me.effectiveRole
  if (role && rank[role] >= rank[needed]) return { allowed: true, reason: null }
  const who = me.grantors && me.grantors.length > 0 ? me.grantors.join(', ') : 'the server’s administrator'
  return { allowed: false, reason: `To ${what} you need the ${needed} role. You have ${role ? `the ${role} role` : 'no role here'}. ${who} can grant it.` }
}
