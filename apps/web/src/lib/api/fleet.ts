import { apiClient } from './client';

// ─── Types ─────────────────────────────────────────────────────────

export type FleetHealthSeverity = 'Healthy' | 'Warning' | 'Critical';

export interface FleetNamespaceHealth {
  namespaceId: string;
  namespaceName: string;
  provider: string;
  environment: string;
  activeCount: number;
  newInWindow: number;
  resolvedInWindow: number;
  totalCount: number;
  topEntity: string | null;
  topEntityCount: number;
  topCategory: string | null;
  oldestActiveDetectedAt: string | null;
  severity: FleetHealthSeverity;
}

export interface FleetTrendPoint {
  date: string;
  newMessages: number;
  resolvedMessages: number;
}

export interface FleetOverview {
  generatedAt: string;
  windowHours: number;
  namespaceCount: number;
  totalActive: number;
  totalNewInWindow: number;
  totalResolvedInWindow: number;
  namespaces: FleetNamespaceHealth[];
  topCategories: Record<string, number>;
  dailyTrend: FleetTrendPoint[];
}

// ─── API Client ────────────────────────────────────────────────────

export const fleetApi = {
  /**
   * Get the cross-namespace fleet operations overview.
   * @param windowHours "recent activity" window in hours (clamped 1–720 server-side).
   */
  getOverview: async (windowHours = 24): Promise<FleetOverview> => {
    const response = await apiClient.get<FleetOverview>('/fleet/overview', {
      params: { windowHours },
    });
    return response.data;
  },
};
