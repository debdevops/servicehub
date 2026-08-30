import { apiClient } from './client';

// ─── Types ─────────────────────────────────────────────────────────────────
//
// Mirrors ServiceHub.Core.DTOs.Responses.MeResponse exactly.

/** The caller's own identity: owner ID, how this request authenticated, and their fleet-wide
 * effective Governance role (null when Governance has never been activated for this owner —
 * see GovernanceGrantsPage's empty state — or when no grant applies fleet-wide). */
export interface MeResponse {
  ownerId: string;
  authMethod: string | null;
  governanceRole: string | null;
}

// ─── API Client ─────────────────────────────────────────────────────────────

export const meApi = {
  /** GET /api/v1/me — no scope requirement, any authenticated caller can read their own identity. */
  getMe: async (): Promise<MeResponse> => {
    const response = await apiClient.get<MeResponse>('/me');
    return response.data;
  },
};
