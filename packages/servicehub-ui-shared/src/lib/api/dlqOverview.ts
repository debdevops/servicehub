import { apiClient } from './client';
import type { CloudProviderType } from './types';

// ─── Types ─────────────────────────────────────────────────────────

// Matches the API's JsonStringEnumConverter (camelCase enum values) — see FailureCategory.cs.
export type DlqFailureCategory =
  | 'unknown'
  | 'transient'
  | 'maxDelivery'
  | 'expired'
  | 'dataQuality'
  | 'authorization'
  | 'processingError'
  | 'resourceNotFound'
  | 'quotaExceeded';

// Matches EnvironmentType.cs's camelCase wire values.
export type DlqOverviewEnvironment = 'dev' | 'uat' | 'prod';

// Matches DlqMessageStatus.cs's camelCase wire values (transient claim states omitted — never
// filterable from the UI).
export type DlqOverviewStatus =
  | 'active'
  | 'replayed'
  | 'archived'
  | 'discarded'
  | 'replayFailed'
  | 'resolved';

// Matches ReplaySafetyLevels.cs's closed vocabulary.
export type DlqReplaySafety = 'Safe' | 'RequiresReview' | 'Unsafe';

// Matches DlqOverviewService's ConfidenceBucket — a coarse label, not a fabricated precision
// score (see DlqRecurringPattern's backend doc comment).
export type DlqPatternConfidence = 'High' | 'Medium' | 'Low';

export interface DlqOverviewTrendPoint {
  date: string;
  count: number;
}

export interface DlqReasonBreakdown {
  category: DlqFailureCategory;
  count: number;
  percent: number;
}

export interface DlqOverviewNamespace {
  namespaceId: string;
  namespaceName: string;
  environment: DlqOverviewEnvironment;
  queuesWithDlq: number;
  topicsWithDlq: number;
  dlqCount: number;
  oldestDetectedAt: string | null;
}

export interface DlqProviderOverview {
  provider: CloudProviderType;
  totalDeadLettered: number;
  changePercent: number | null;
  namespacesWithDlq: number;
  namespacesTotal: number;
  affectedQueues: number;
  affectedTopics: number;
  dailyTrend: DlqOverviewTrendPoint[];
  topReasons: DlqReasonBreakdown[];
  namespaces: DlqOverviewNamespace[];
}

export interface DlqOverviewTotals {
  totalDeadLettered: number;
  changePercent: number | null;
  namespacesWithDlq: number;
  namespacesTotal: number;
  affectedQueues: number;
  affectedTopics: number;
  oldestMessageDetectedAt: string | null;
  replayedCount: number;
  replayedChangePercent: number | null;
  archivedCount: number;
  totalObserved: number;
  recurringPatternCount: number;
  needsInvestigationCount: number;
}

/**
 * A recurring failure pattern observed across the fleet — active messages sharing the same
 * category and forensic root-cause text, regardless of provider or namespace. See the backend's
 * `DlqRecurringPattern` doc comment for why this is grounded in already-computed per-message
 * fields rather than the AI clustering pipeline behind the per-namespace Failure Signatures view.
 */
export interface DlqRecurringPattern {
  patternKey: string;
  rootCauseSummary: string;
  category: DlqFailureCategory;
  occurrences: number;
  percentOfTotal: number;
  affectedProviders: CloudProviderType[];
  namespaceCount: number;
  firstSeenAt: string;
  lastSeenAt: string;
  confidence: DlqPatternConfidence;
  averageConfidence: number;
  replaySafety: DlqReplaySafety;
  suggestedAction: string;
  representativeNamespaceId: string;
  representativeEntityName: string;
}

export interface DlqOverview {
  generatedAt: string;
  windowDays: number;
  totals: DlqOverviewTotals;
  providers: DlqProviderOverview[];
  recurringPatterns: DlqRecurringPattern[];
}

export interface DlqOverviewParams {
  days?: number;
  cloud?: CloudProviderType;
  environment?: DlqOverviewEnvironment;
  namespaceId?: string;
  reason?: DlqFailureCategory;
  entityName?: string;
  status?: DlqOverviewStatus;
  replaySafety?: DlqReplaySafety;
}

// ─── API Client ────────────────────────────────────────────────────

export const dlqOverviewApi = {
  /** Get the cross-cloud, provider-grouped DLQ overview. */
  getOverview: async (params: DlqOverviewParams = {}): Promise<DlqOverview> => {
    const response = await apiClient.get<DlqOverview>('/dlq/overview', { params });
    return response.data;
  },
};
