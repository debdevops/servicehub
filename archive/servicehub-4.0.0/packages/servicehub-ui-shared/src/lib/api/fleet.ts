import { apiClient } from './client';

// ─── Types ─────────────────────────────────────────────────────────

// Matches the wire value: the API's JsonStringEnumConverter serializes enums camelCase,
// so FleetHealthSeverity.Healthy arrives as "healthy", not "Healthy".
// 'unknown' means "zero known dead-letters, but coverage isn't Scanned" — it must never be
// treated as equivalent to 'healthy'.
export type FleetHealthSeverity = 'healthy' | 'warning' | 'critical' | 'unknown';

// Whether the namespace's dead-letter activity reflects a real background scan, or an absence
// of scanning that must not be mistaken for a clean queue (mirrors the backend's
// FleetMonitoringCoverage — see FleetOverviewService.DetermineCoverage).
export type FleetMonitoringCoverage = 'scanned' | 'notMonitored' | 'providerNotRegistered';

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
  coverage: FleetMonitoringCoverage;
  coverageNote: string | null;
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
