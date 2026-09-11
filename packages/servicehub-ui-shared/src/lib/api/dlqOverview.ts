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
}

export interface DlqOverview {
  generatedAt: string;
  windowDays: number;
  totals: DlqOverviewTotals;
  providers: DlqProviderOverview[];
}

export interface DlqOverviewParams {
  days?: number;
  cloud?: CloudProviderType;
  environment?: DlqOverviewEnvironment;
  namespaceId?: string;
  reason?: DlqFailureCategory;
}

// ─── API Client ────────────────────────────────────────────────────

export const dlqOverviewApi = {
  /** Get the cross-cloud, provider-grouped DLQ overview. */
  getOverview: async (params: DlqOverviewParams = {}): Promise<DlqOverview> => {
    const response = await apiClient.get<DlqOverview>('/dlq/overview', { params });
    return response.data;
  },
};
