import { describe, it, expect } from 'vitest';
import { evaluateGrantee, groupGrantees, roleMeets } from '@/components/autonomy/governanceModel';
import type { GovernanceGrant } from '@servicehub/ui-shared/lib/api/governance';

function grant(overrides: Partial<GovernanceGrant> = {}): GovernanceGrant {
  return {
    id: `g-${Math.random()}`,
    granteeIdentity: 'entra:alex',
    granteeKind: 'User',
    role: 'Viewer',
    namespaceId: null,
    pillarKind: null,
    grantedAt: '2026-09-01T00:00:00Z',
    grantedByIdentity: 'System:Seed',
    revokedAt: null,
    revokedByIdentity: null,
    ...overrides,
  };
}

function allowed(grants: GovernanceGrant[], id: string) {
  return evaluateGrantee(grants).find(a => a.action.id === id)!;
}

describe('roleMeets', () => {
  it('orders roles Viewer < Operator < Approver < Admin, as the backend does', () => {
    expect(roleMeets('Approver', 'Operator')).toBe(true);
    expect(roleMeets('Operator', 'Approver')).toBe(false);
    expect(roleMeets('Admin', 'Admin')).toBe(true);
  });
});

describe('evaluateGrantee', () => {
  it('never lets any role — even a fleet-wide Admin — set autonomy or bypass safety', () => {
    const admin = [grant({ role: 'Admin' })];
    expect(allowed(admin, 'set-autonomy').allowed).toBe(false);
    expect(allowed(admin, 'bypass-safety').allowed).toBe(false);
    expect(allowed(admin, 'grants').allowed).toBe(true);
  });

  it('scopes a namespace-scoped Operator grant to that namespace, and not to fleet-wide actions', () => {
    const grants = [grant({ role: 'Operator', namespaceId: 'ns-1', pillarKind: 'Recover' })];
    expect(allowed(grants, 'replay')).toMatchObject({ allowed: true, where: '1 namespace' });
    expect(allowed(grants, 'playbook').allowed).toBe(false);
  });

  it('does not let an Investigate-only grant replay (a Recover-pillar action)', () => {
    expect(allowed([grant({ role: 'Admin', pillarKind: 'Investigate', namespaceId: 'ns-1' })], 'replay').allowed).toBe(false);
  });

  it('requires a fleet-wide grant for fleet-wide actions', () => {
    expect(allowed([grant({ role: 'Admin', namespaceId: 'ns-1' })], 'emergency-stop').allowed).toBe(false);
  });

  it('ignores revoked grants', () => {
    expect(allowed([grant({ role: 'Admin', revokedAt: '2026-09-02T00:00:00Z' })], 'grants').allowed).toBe(false);
  });
});

describe('API payloads that omit null fields', () => {
  // The API drops null properties from JSON, so an unset namespace/pillar arrives as undefined.
  const omitted = { ...grant({ role: 'Admin' }) } as Partial<GovernanceGrant>;
  delete omitted.namespaceId;
  delete omitted.pillarKind;
  delete omitted.revokedAt;

  it('treats a missing namespace and pillar as fleet-wide and all pillars', () => {
    const [person] = groupGrantees([omitted as GovernanceGrant]);
    expect(person).toMatchObject({ fleetWide: true, namespaceIds: [], pillars: null });
    expect(allowed([omitted as GovernanceGrant], 'grants')).toMatchObject({ allowed: true, where: 'Fleet-wide' });
  });
});

describe('groupGrantees', () => {
  it('groups by identity, drops revoked grants, and reports the highest role', () => {
    const people = groupGrantees([
      grant({ role: 'Viewer', namespaceId: 'ns-1' }),
      grant({ role: 'Approver', namespaceId: 'ns-2', pillarKind: 'Correlate' }),
      grant({ granteeIdentity: 'ApiKey:ci', granteeKind: 'ApiKey', role: 'Admin', revokedAt: '2026-09-02T00:00:00Z' }),
    ]);
    expect(people).toHaveLength(1);
    expect(people[0]).toMatchObject({ identity: 'entra:alex', highestRole: 'Approver', fleetWide: false, namespaceIds: ['ns-1', 'ns-2'] });
    expect(people[0].pillars).toBeNull(); // the Viewer grant covers all pillars
  });
});
