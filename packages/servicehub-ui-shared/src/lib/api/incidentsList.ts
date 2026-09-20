import { apiClient } from './client';
import type { CloudProviderType } from './types';

// ─── Types ─────────────────────────────────────────────────────────────────
// Mirrors ServiceHub.Core.DTOs.Responses.IncidentListResponses.cs exactly.

/** Fleet-wide compact metrics — mirrors CompactMetricsSummary (InvestigationCenterResponses.cs). */
export interface IncidentListMetrics {
  totalSignatures: number;
  activeSignatures: number;
  resolvedSignatures: number;
  suppressedSignatures: number;
  archivedSignatures: number;
  requiresAction: number;
}

/**
 * One bucket of the incident trend chart. See the backend's `IncidentTrendPoint` doc comment for
 * exactly what `active`/`resolved`/`new` mean — real timestamps bucketed, not a reconstructed
 * point-in-time snapshot.
 */
export interface IncidentTrendPoint {
  bucketStart: string;
  active: number;
  resolved: number;
  new: number;
}

/** One entry of the "Top Incident Categories" rollup — total message occurrences, not signature count. */
export interface IncidentCategoryBreakdown {
  category: string;
  count: number;
  percent: number;
}

export type IncidentSeverity = 'Critical' | 'High' | 'Medium' | 'Low';

/** One row of the Incident Center's list/table. */
export interface IncidentListItem {
  signatureHash: string;
  namespaceId: string;
  namespaceName: string | null;
  cloudProvider: CloudProviderType | null;
  environment: string | null;
  displayName: string;
  category: string;
  severity: IncidentSeverity;
  status: string;
  isEscalating: boolean;
  messageCount: number;
  firstSeenAt: string;
  lastSeenAt: string;
  hasKnowledge: boolean;
  owner: string | null;
  recommendedNextAction: string | null;
}

export interface IncidentListResponse {
  metrics: IncidentListMetrics;
  trend: IncidentTrendPoint[];
  topCategories: IncidentCategoryBreakdown[];
  items: IncidentListItem[];
  generatedAt: string;
}

// ─── API Client ────────────────────────────────────────────────────────────

export const incidentsListApi = {
  /** GET /api/v1/failure-intelligence/incidents?days= */
  get: async (days: number): Promise<IncidentListResponse> => {
    const response = await apiClient.get<IncidentListResponse>('/failure-intelligence/incidents', {
      params: { days },
    });
    return response.data;
  },
};
