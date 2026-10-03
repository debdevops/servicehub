import { apiClient } from './client';

// ─── Types ─────────────────────────────────────────────────────────────────
//
// Mirrors the backend Governance DTOs (ServiceHub.Core.DTOs.{Requests,Responses}) exactly —
// string enum members are PascalCase, matching the backend's `.ToString()` serialization, not
// camelCase.

export type GranteeKind = 'User' | 'ApiKey';

export type GovernanceRole = 'Viewer' | 'Operator' | 'Approver' | 'Admin';

export type PillarKind = 'Recover' | 'Investigate' | 'Correlate' | 'Prevent';

export interface GovernanceGrant {
  id: string;
  granteeIdentity: string;
  granteeKind: GranteeKind;
  role: GovernanceRole;
  namespaceId: string | null;
  pillarKind: PillarKind | null;
  grantedAt: string;
  grantedByIdentity: string;
  revokedAt: string | null;
  revokedByIdentity: string | null;
}

export interface GrantGovernanceRoleRequest {
  granteeIdentity: string;
  granteeKind: GranteeKind;
  role: GovernanceRole;
  namespaceId?: string | null;
  pillarKind?: PillarKind | null;
}

/** One-line meaning per role, for a tooltip/help affordance next to the role picker. */
export const GOVERNANCE_ROLE_EXPLANATIONS: Record<GovernanceRole, string> = {
  Viewer: 'Read-only access.',
  Operator: 'Can create/enable rules and execute approvals within existing autonomy limits.',
  Approver: 'Can act on the L3 approval queue and Playbook Ledger disposition specifically.',
  Admin: 'Can manage namespaces and Governance grants themselves, fleet-wide by default.',
};

// ─── API Client ─────────────────────────────────────────────────────────────

export const governanceApi = {
  getGrants: async (granteeIdentity?: string): Promise<GovernanceGrant[]> => {
    const response = await apiClient.get<GovernanceGrant[]>('/governance/grants', {
      params: granteeIdentity ? { granteeIdentity } : undefined,
    });
    return response.data;
  },

  grant: async (request: GrantGovernanceRoleRequest): Promise<GovernanceGrant> => {
    const response = await apiClient.post<GovernanceGrant>('/governance/grants', request);
    return response.data;
  },

  revoke: async (id: string): Promise<void> => {
    await apiClient.post(`/governance/grants/${id}/revoke`);
  },
};
