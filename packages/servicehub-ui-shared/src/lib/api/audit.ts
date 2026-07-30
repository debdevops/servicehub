import { apiClient } from './client';

// ─── Types ─────────────────────────────────────────────────────────────────

export interface AuditLogItem {
  id: string;
  timestamp: string;
  userIdentity: string;
  action: string;
  outcome: 'Success' | 'Failure' | 'Partial';
  namespaceId: string | null;
  namespaceName: string | null;
  entityName: string | null;
  cloudProvider: string | null;
  environment: string | null;
  resourceName: string | null;
  sequenceNumber: number | null;
  detailsJson: string | null;
  errorDetails: string | null;
  clientIp: string | null;
  userAgent: string | null;
  correlationId: string | null;
  httpMethod: string | null;
  httpPath: string | null;
}

export interface AuditPageResponse {
  items: AuditLogItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

export interface AuditSummaryResponse {
  totalEvents: number;
  successCount: number;
  failureCount: number;
  partialCount: number;
  activeUsers: number;
  successRate: number;
}

export interface AuditParams {
  namespaceId?: string;
  search?: string;
  actionType?: string;
  outcome?: string;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}

// ─── API Client ─────────────────────────────────────────────────────────────

export const auditApi = {
  /**
   * Get paginated audit log entries.
   */
  getLogs: async (params: AuditParams): Promise<AuditPageResponse> => {
    const response = await apiClient.get<AuditPageResponse>('/audit', { params });
    return response.data;
  },

  /**
   * Get audit trail summary statistics.
   */
  getSummary: async (namespaceId?: string): Promise<AuditSummaryResponse> => {
    const response = await apiClient.get<AuditSummaryResponse>('/audit/summary', {
      params: namespaceId ? { namespaceId } : undefined,
    });
    return response.data;
  },

  /**
   * Download audit log export as CSV or JSON file.
   * Uses the axios client so SPA auth headers are included.
   */
  downloadExport: async (
    format: 'csv' | 'json' = 'csv',
    params?: Omit<AuditParams, 'page' | 'pageSize'>
  ): Promise<void> => {
    const queryParams = new URLSearchParams({ format });
    if (params?.namespaceId) queryParams.set('namespaceId', params.namespaceId);
    if (params?.actionType) queryParams.set('actionType', params.actionType);
    if (params?.outcome) queryParams.set('outcome', params.outcome);
    if (params?.from) queryParams.set('from', params.from);
    if (params?.to) queryParams.set('to', params.to);

    const response = await apiClient.get(`/audit/export?${queryParams.toString()}`, {
      responseType: 'blob',
    });

    const mimeType = format === 'csv' ? 'text/csv' : 'application/json';
    const blob = new Blob([response.data as BlobPart], { type: mimeType });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = `audit-export-${new Date().toISOString().slice(0, 10)}.${format}`;
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);
    // Defer cleanup to prevent Safari download cancellation
    setTimeout(() => URL.revokeObjectURL(url), 0);
  },
};
