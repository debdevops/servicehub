import type { GovernanceGrant, GovernanceRole, GranteeKind, PillarKind } from '@servicehub/ui-shared/lib/api/governance';

/**
 * The Governance permission model as the backend actually enforces it — a read-only mirror used to
 * explain "who can do what, where", never to decide it. Every row below cites the check that
 * enforces it; the server is always the authority (GovernanceAccessEvaluator: roles are ordered
 * Viewer < Operator < Approver < Admin, a grant with no namespace is fleet-wide, a grant with no
 * pillar covers all four, and a caller gets the highest role among their own applicable grants).
 */

// The API omits null properties from its JSON, so an "unset" namespace/pillar/revocation can
// arrive as `undefined` rather than `null` — every check below treats the two the same (`== null`).

export const ROLE_ORDER: readonly GovernanceRole[] = ['Viewer', 'Operator', 'Approver', 'Admin'];

export function roleRank(role: GovernanceRole): number {
  return ROLE_ORDER.indexOf(role);
}

export function roleMeets(role: GovernanceRole, minimum: GovernanceRole): boolean {
  return roleRank(role) >= roleRank(minimum);
}

export function isGovernanceRole(value: string | null | undefined): value is GovernanceRole {
  return !!value && (ROLE_ORDER as readonly string[]).includes(value);
}

export interface GovernedAction {
  id: string;
  label: string;
  /** Minimum role, or `null` when no role can do it — by design. */
  minRole: GovernanceRole | null;
  /** The pillar the check is scoped to; `'any'` when it follows the item's own pillar. */
  pillar: PillarKind | 'any' | null;
  /** `'fleet'` actions need a fleet-wide grant; `'namespace'` ones can be granted per namespace. */
  scope: 'fleet' | 'namespace';
  enforcedBy: string;
}

export const GOVERNED_ACTIONS: readonly GovernedAction[] = [
  {
    id: 'replay',
    label: 'Replay messages, including approving from the Approval Queue',
    minRole: 'Operator',
    pillar: 'Recover',
    scope: 'namespace',
    enforcedBy: 'Replay and signature-replay endpoints',
  },
  {
    id: 'write-off',
    label: 'Write off a stuck recovery entry',
    minRole: 'Operator',
    pillar: 'Recover',
    scope: 'namespace',
    enforcedBy: 'Recovery write-off endpoint',
  },
  {
    id: 'elevation-request',
    label: 'Request a time-boxed production elevation',
    minRole: 'Operator',
    pillar: 'Recover',
    scope: 'namespace',
    enforcedBy: 'Production elevation endpoint',
  },
  {
    id: 'playbook',
    label: 'Approve or reject Playbook proposals',
    minRole: 'Approver',
    pillar: 'any',
    scope: 'namespace',
    enforcedBy: 'Playbook disposition endpoint (per proposal namespace and pillar)',
  },
  {
    id: 'elevation-approve',
    label: "Approve someone else's production elevation",
    minRole: 'Approver',
    pillar: 'Recover',
    scope: 'namespace',
    enforcedBy: 'Production elevation approval (requester can never self-approve)',
  },
  {
    id: 'grants',
    label: 'Grant and revoke Governance roles',
    minRole: 'Admin',
    pillar: null,
    scope: 'fleet',
    enforcedBy: 'Governance endpoints (admin scope + Admin role)',
  },
  {
    id: 'emergency-stop',
    label: 'Activate or clear the emergency stop',
    minRole: 'Admin',
    pillar: null,
    scope: 'fleet',
    enforcedBy: 'Emergency-stop endpoints (admin scope + Admin role)',
  },
  {
    id: 'set-autonomy',
    label: "Set or raise a signature's autonomy level",
    minRole: null,
    pillar: null,
    scope: 'fleet',
    enforcedBy: 'No endpoint exists — autonomy is earned only from verified recovery evidence',
  },
  {
    id: 'bypass-safety',
    label: 'Bypass the Eligibility Gate, circuit breakers or production floor',
    minRole: null,
    pillar: null,
    scope: 'fleet',
    enforcedBy: 'No endpoint exists — the gate runs on every recovery path',
  },
];

export interface GrantedAction {
  action: GovernedAction;
  allowed: boolean;
  /** Where the allowance applies, e.g. "Fleet-wide" or "2 namespaces". Empty when not allowed. */
  where: string;
}

function grantCovers(grant: GovernanceGrant, action: GovernedAction): boolean {
  if (!action.minRole || grant.revokedAt) return false;
  if (!roleMeets(grant.role, action.minRole)) return false;
  if (action.scope === 'fleet' && grant.namespaceId != null) return false;
  if (action.pillar && action.pillar !== 'any' && grant.pillarKind != null && grant.pillarKind !== action.pillar) return false;
  return true;
}

/** What one grantee's own active grants allow, action by action. */
export function evaluateGrantee(grants: readonly GovernanceGrant[]): GrantedAction[] {
  return GOVERNED_ACTIONS.map(action => {
    const covering = grants.filter(g => grantCovers(g, action));
    if (covering.length === 0) return { action, allowed: false, where: '' };
    if (covering.some(g => g.namespaceId == null)) return { action, allowed: true, where: 'Fleet-wide' };
    const namespaces = new Set(covering.map(g => g.namespaceId));
    return { action, allowed: true, where: `${namespaces.size} namespace${namespaces.size === 1 ? '' : 's'}` };
  });
}

export interface Grantee {
  identity: string;
  kind: GranteeKind;
  grants: GovernanceGrant[];
  highestRole: GovernanceRole;
  fleetWide: boolean;
  namespaceIds: string[];
  /** `null` when at least one grant covers every pillar. */
  pillars: PillarKind[] | null;
}

/** `__spa__` is the identity the ServiceHub web UI authenticates as — the seeded owner-level grant. */
export function describeGrantee(identity: string): string | null {
  return identity === '__spa__' ? 'ServiceHub web UI (owner)' : null;
}

/** Groups active grants by grantee identity — the "people & keys" view. */
export function groupGrantees(grants: readonly GovernanceGrant[]): Grantee[] {
  const byIdentity = new Map<string, GovernanceGrant[]>();
  for (const grant of grants) {
    if (grant.revokedAt) continue;
    const list = byIdentity.get(grant.granteeIdentity);
    if (list) list.push(grant);
    else byIdentity.set(grant.granteeIdentity, [grant]);
  }
  const result: Grantee[] = [];
  for (const [identity, list] of byIdentity) {
    const highestRole = list.reduce<GovernanceRole>((best, g) => (roleRank(g.role) > roleRank(best) ? g.role : best), 'Viewer');
    const allPillars = list.some(g => g.pillarKind == null);
    result.push({
      identity,
      kind: list[0].granteeKind,
      grants: list,
      highestRole,
      fleetWide: list.some(g => g.namespaceId == null),
      namespaceIds: Array.from(new Set(list.map(g => g.namespaceId).filter((v): v is string => v != null))),
      pillars: allPillars ? null : Array.from(new Set(list.map(g => g.pillarKind as PillarKind))),
    });
  }
  return result.sort((a, b) => roleRank(b.highestRole) - roleRank(a.highestRole) || a.identity.localeCompare(b.identity));
}

export const ROLE_TONE: Record<GovernanceRole, 'gray' | 'blue' | 'violet' | 'red'> = {
  Viewer: 'gray',
  Operator: 'blue',
  Approver: 'violet',
  Admin: 'red',
};
