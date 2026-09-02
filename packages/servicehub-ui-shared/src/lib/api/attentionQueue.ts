import { apiClient } from './client';

// ─── Types ─────────────────────────────────────────────────────────

/** Mirrors ServiceHub.Core.DTOs.Responses.AttentionQueueItem — string enums are `.ToString()`
 * (PascalCase) on the wire, not JsonStringEnumConverter output, since they're plain `string`
 * properties on the DTO, not enum-typed ones. */
export interface AttentionQueueItem {
  signatureHash: string;
  namespaceId: string;
  namespaceName: string | null;
  displayName: string;
  lifecycleStatus: string;
  severity: 'Critical' | 'Warning' | 'Healthy' | 'Unknown';
  blastRadius: number;
  isRecurring: boolean;
  pendingDecisionCount: number;
  score: number;
  recommendedAction: string;
  lastSeenAt: string;
}

export interface AttentionQueueResponse {
  items: AttentionQueueItem[];
  isEmpty: boolean;
}

// ─── API Client ────────────────────────────────────────────────────

export const attentionQueueApi = {
  /** Home as a ranked attention queue (roadmap W2.2) — up to three signatures across every
   * namespace the caller owns, ranked by severity, blast radius, recurrence, and whether a
   * human decision is blocking. */
  get: async (): Promise<AttentionQueueResponse> => {
    const response = await apiClient.get<AttentionQueueResponse>('/attention-queue');
    return response.data;
  },
};
