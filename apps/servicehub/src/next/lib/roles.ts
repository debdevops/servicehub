import type { Grant } from '../../lib/api/settings'

/**
 * What each role may do — read from the server's own gates (`DeniedUnlessAsync(GovernanceRole.…)` in the API controllers), not from a
 * guess. Reading is open to every role: a role limits actions and never hides what a person may already read.
 *
 *   Operator  replay, purge, Look now, bulk replay; switch an Auto Replay rule OFF or delete it
 *   Approver  everything an Operator may, plus approve or decline what the Agent asked, and create, change or switch ON a rule
 *   Admin     everything above, plus roles, connections, notification channels, backups and the emergency stop
 *
 * If a gate changes on the server this table must change with it. `tests/web/unit/next/roles.test.ts` pins that a higher role never
 * loses an ability a lower one has, which is the property the server's ranking guarantees.
 */
export type Ability = 'read' | 'replay' | 'approve' | 'rules' | 'access'
export type Allowance = true | false | string

export const abilities: readonly { readonly id: Ability; readonly label: string }[] = [
  { id: 'read', label: 'See everything' }, { id: 'replay', label: 'Replay' }, { id: 'approve', label: 'Approve or decline' },
  { id: 'rules', label: 'Change rules' }, { id: 'access', label: 'Manage access' },
]

export const roleRows: readonly { readonly role: 'Viewer' | 'Operator' | 'Approver' | 'Admin'; readonly can: Readonly<Record<Ability, Allowance>> }[] = [
  { role: 'Viewer', can: { read: true, replay: false, approve: false, rules: false, access: false } },
  { role: 'Operator', can: { read: true, replay: true, approve: false, rules: 'Switch off or delete only', access: false } },
  { role: 'Approver', can: { read: true, replay: true, approve: true, rules: true, access: false } },
  { role: 'Admin', can: { read: true, replay: true, approve: true, rules: true, access: true } },
]

/** A person or an API key, in words. */
export const granteeWords = (kind: Grant['granteeKind']): string => (kind === 'ApiKey' ? 'API key' : 'Person')

/** What a grant covers. A grant with no connection covers everything the grantee can reach; one with a recovery pillar only that. */
export function grantScope(grant: Pick<Grant, 'namespaceId' | 'pillarKind'>): string {
  const where = grant.namespaceId === null ? 'Everything' : 'This connection'
  return grant.pillarKind ? `${where}, ${grant.pillarKind.toLowerCase()} only` : where
}
