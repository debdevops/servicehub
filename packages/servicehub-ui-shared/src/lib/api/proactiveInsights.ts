import { apiClient } from './client';

// ─── Types ─────────────────────────────────────────────────────────────────
//
// Mirrors the backend proactive-insights response DTOs exactly (field names
// camelCase per the default JSON serializer; string enum members stay
// PascalCase, matching the backend's `.ToString()` serialization) —
// NarrationsController, CorrelationFindingsController, BacklogForecastsController,
// and DriftFindingsController.Export in ServiceHub.Api.Controllers.V1.

/** Mirrors ServiceHub.Core.Enums.NarrationKind. */
export type NarrationKind = 'NamespaceActivity' | 'CrossNamespaceCorrelation';

/** Mirrors ServiceHub.Api.Controllers.V1.NarrationInfo. */
export interface NarrationInfo {
  id: string;
  kind: NarrationKind;
  namespaceId: string | null;
  headline: string;
  summary: string;
  severity: number;
  generatedAt: string;
  recommendedActions: string[];
}

/** Mirrors ServiceHub.Api.Controllers.V1.NarrationGenerationResponse. */
export interface NarrationGenerationResponse {
  startTime: string;
  endTime: string;
  narrations: NarrationInfo[];
  generatedAt: string;
}

/** Mirrors ServiceHub.Api.Controllers.V1.CorrelationMemberInfo. */
export interface CorrelationMemberInfo {
  namespaceId: string;
  entityName: string;
  anomalyType: string;
  severity: number;
  provider: string;
}

/**
 * Mirrors ServiceHub.Api.Controllers.V1.CorrelationFindingInfo. `providers` has one entry for a
 * same-provider (C1) finding and two or more for a cross-cloud (C2) finding.
 */
export interface CorrelationFindingInfo {
  id: string;
  providers: string[];
  members: CorrelationMemberInfo[];
  severity: number;
  description: string;
  detectedAt: string;
  metrics: Record<string, number>;
  recommendedActions: string[];
}

/** Mirrors ServiceHub.Api.Controllers.V1.CorrelationDetectionResponse. */
export interface CorrelationDetectionResponse {
  startTime: string;
  endTime: string;
  findings: CorrelationFindingInfo[];
  detectedAt: string;
}

/** Mirrors ServiceHub.Api.Controllers.V1.BacklogForecastInfo. */
export interface BacklogForecastInfo {
  id: string;
  namespaceId: string;
  entityName: string;
  currentBacklogCount: number;
  growthRatePerHour: number;
  alertThreshold: number;
  projectedHoursToBreach: number;
  projectedBreachAtUtc: string;
  severity: number;
  description: string;
  detectedAt: string;
  metrics: Record<string, number>;
  recommendedActions: string[];
}

/** Mirrors ServiceHub.Api.Controllers.V1.BacklogForecastResponse. */
export interface BacklogForecastResponse {
  namespaceId: string;
  startTime: string;
  endTime: string;
  forecasts: BacklogForecastInfo[];
  detectedAt: string;
}

/** Mirrors ServiceHub.Api.Controllers.V1.ContractViolationEntryInfo. */
export interface ContractViolationEntryInfo {
  entityName: string;
  violationType: string;
  priority: string;
  evidence: string;
  suggestedFixes: string[];
}

/** Mirrors ServiceHub.Api.Controllers.V1.ContractViolationExportResponse. */
export interface ContractViolationExportResponse {
  namespaceId: string;
  namespaceName: string;
  startTime: string;
  endTime: string;
  generatedAt: string;
  violations: ContractViolationEntryInfo[];
  markdownReport: string;
}

export interface InsightWindowParams {
  startTime?: string;
  endTime?: string;
}

// ─── API Client ─────────────────────────────────────────────────────────────

export const narrationsApi = {
  /** POST /narrations/generate — stitches I1-I3's structured output into plain-English narrations. */
  generate: async (params: InsightWindowParams = {}): Promise<NarrationGenerationResponse> => {
    const response = await apiClient.post<NarrationGenerationResponse>(
      '/narrations/generate',
      null,
      { params },
    );
    return response.data;
  },
};

export const correlationFindingsApi = {
  /** POST /correlation-findings/detect — same-provider (C1) and cross-cloud (C2) correlation. */
  detect: async (params: InsightWindowParams = {}): Promise<CorrelationDetectionResponse> => {
    const response = await apiClient.post<CorrelationDetectionResponse>(
      '/correlation-findings/detect',
      null,
      { params },
    );
    return response.data;
  },
};

export interface ForecastBacklogParams extends InsightWindowParams {
  namespaceId: string;
  alertThreshold?: number;
}

export const backlogForecastsApi = {
  /** POST /backlog-forecasts/forecast — arithmetic growth-rate projection for one namespace. */
  forecast: async ({ namespaceId, ...rest }: ForecastBacklogParams): Promise<BacklogForecastResponse> => {
    const response = await apiClient.post<BacklogForecastResponse>(
      '/backlog-forecasts/forecast',
      null,
      { params: { namespaceId, ...rest } },
    );
    return response.data;
  },
};

export interface ExportContractViolationsParams extends InsightWindowParams {
  namespaceId: string;
}

export const driftFindingsApi = {
  /** POST /drift-findings/export — producer-facing contract-violation export for one namespace. */
  exportContractViolations: async (
    { namespaceId, ...rest }: ExportContractViolationsParams,
  ): Promise<ContractViolationExportResponse> => {
    const response = await apiClient.post<ContractViolationExportResponse>(
      '/drift-findings/export',
      null,
      { params: { namespaceId, ...rest } },
    );
    return response.data;
  },
};
