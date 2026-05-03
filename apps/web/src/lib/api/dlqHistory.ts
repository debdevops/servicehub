import { apiClient } from './client';

// ─── Types ─────────────────────────────────────────────────────────

export interface DlqHistoryItem {
  id: number;
  messageId: string;
  sequenceNumber: number;
  bodyHash: string;
  namespaceId: string;
  entityName: string;
  entityType: string;
  enqueuedTimeUtc: string;
  deadLetterTimeUtc: string | null;
  detectedAtUtc: string;
  deadLetterReason: string | null;
  deadLetterErrorDescription: string | null;
  deliveryCount: number;
  contentType: string | null;
  messageSize: number;
  bodyPreview: string | null;
  failureCategory: string;
  categoryConfidence: number;
  status: string;
  replayedAt: string | null;
  replaySuccess: boolean | null;
  archivedAt: string | null;
  userNotes: string | null;
  correlationId: string | null;
  topicName: string | null;
  forensicRootCause: string | null;
  forensicConfidence: number;
  replaySafety: string | null;
  cloudProvider?: string;
}

export interface DlqMessageDetail extends DlqHistoryItem {
  applicationPropertiesJson: string | null;
  sessionId: string | null;
  replayHistory: ReplayHistoryItem[];
}

export interface ReplayHistoryItem {
  id: number;
  replayedAt: string;
  replayedBy: string;
  replayStrategy: string;
  replayedToEntity: string;
  outcomeStatus: string;
  newDeadLetterReason: string | null;
  errorDetails: string | null;
}

export interface DlqTimelineEvent {
  eventType: string;
  description: string;
  timestamp: string;
  details: Record<string, string> | null;
}

export interface DlqTimelineResponse {
  messageId: number;
  entityName: string;
  events: DlqTimelineEvent[];
}

export interface DlqSummary {
  totalMessages: number;
  activeMessages: number;
  replayedMessages: number;
  archivedMessages: number;
  byCategory: Record<string, number>;
  byEntity: Record<string, number>;
  oldestMessage: string | null;
  newestMessage: string | null;
  dailyTrend: DlqTrendPoint[];
}

export interface DlqTrendPoint {
  date: string;
  newMessages: number;
  resolvedMessages: number;
}

export interface PaginatedResponse<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

export interface DlqHistoryParams {
  namespaceId?: string;
  entityName?: string;
  from?: string;
  to?: string;
  status?: string;
  category?: string;
  page?: number;
  pageSize?: number;
}

// ─── API Client ────────────────────────────────────────────────────
/**
 * Sparkline data point for daily DLQ trend visualization.
 * Used in getTrend() for dashboard charts.
 *//**
 * Sparkline data point for daily DLQ trend visualization.
 * Used in getTrend() for dashboard charts.
 */
export interface DlqSparklinePoint {
  date: string;
  newCount: number;
  resolvedCount: number;
}

export const dlqHistoryApi = {
  /**
   * Get paginated DLQ message history.
   */
  getHistory: async (params: DlqHistoryParams): Promise<PaginatedResponse<DlqHistoryItem>> => {
    const response = await apiClient.get<PaginatedResponse<DlqHistoryItem>>(
      '/dlq/history',
      { params }
    );
    return response.data;
  },

  /**
   * Get a single DLQ message with full details.
   */
  getById: async (id: number): Promise<DlqMessageDetail> => {
    const response = await apiClient.get<DlqMessageDetail>(`/dlq/history/${id}`);
    return response.data;
  },

  /**
   * Get the timeline for a DLQ message.
   */
  getTimeline: async (id: number): Promise<DlqTimelineResponse> => {
    const response = await apiClient.get<DlqTimelineResponse>(`/dlq/history/${id}/timeline`);
    return response.data;
  },

  /**
   * Update notes on a DLQ message.
   */
  updateNotes: async (id: number, notes: string): Promise<DlqHistoryItem> => {
    const response = await apiClient.post<DlqHistoryItem>(`/dlq/history/${id}/notes`, { notes });
    return response.data;
  },

  /**
   * Trigger an immediate DLQ scan for a namespace.
   */
  triggerScan: async (namespaceId: string): Promise<number> => {
    const response = await apiClient.post<number>(`/dlq/scan/${namespaceId}`);
    return response.data;
  },

  /**
   * Get DLQ summary statistics.
   */
  getSummary: async (namespaceId?: string): Promise<DlqSummary> => {
    const response = await apiClient.get<DlqSummary>('/dlq/summary', {
      params: namespaceId ? { namespaceId } : undefined,
    });
    return response.data;
  },

  /**
   * Get DLQ trend data for sparklines (daily new/resolved counts).
   */
  getTrend: async (namespaceId: string, days: number = 7): Promise<DlqSparklinePoint[]> => {
    const response = await apiClient.get<Array<{ date: string; newMessages: number; resolvedMessages: number }>>(
      '/dlq/trend',
      { params: { namespaceId, days } }
    );
    return response.data.map(d => ({
      date: d.date,
      newCount: d.newMessages,
      resolvedCount: d.resolvedMessages,
    })) as DlqSparklinePoint[];
  },

  /**
   * Download a DLQ export via the axios client (includes SPA auth token).
   * Creates a Blob URL and triggers browser download — no window.open needed.
   */
  downloadExport: async (format: 'json' | 'csv' = 'json', params?: DlqHistoryParams): Promise<void> => {
    const queryParams = new URLSearchParams({ format });
    if (params?.namespaceId) queryParams.set('namespaceId', params.namespaceId);
    if (params?.entityName) queryParams.set('entityName', params.entityName);
    if (params?.from) queryParams.set('from', params.from);
    if (params?.to) queryParams.set('to', params.to);
    if (params?.status) queryParams.set('status', params.status);

    const response = await apiClient.get(`/dlq/export?${queryParams.toString()}`, {
      responseType: 'blob',
    });

    const mimeType = format === 'csv' ? 'text/csv' : 'application/json';
    const blob = new Blob([response.data as BlobPart], { type: mimeType });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = `dlq-export-${new Date().toISOString().slice(0, 10)}.${format}`;
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);
    // Defer URL cleanup to prevent Safari download cancellation
    setTimeout(() => {
      URL.revokeObjectURL(url);
    }, 0);
  },

  /**
   * Get export download URL (kept for backwards compatibility).
   * @deprecated Use downloadExport() instead — window.open bypasses SPA auth headers.
   */
  getExportUrl: (format: 'json' | 'csv' = 'json', params?: DlqHistoryParams): string => {
    const baseUrl = apiClient.defaults.baseURL || '';
    const queryParams = new URLSearchParams({ format });
    if (params?.namespaceId) queryParams.set('namespaceId', params.namespaceId);
    if (params?.entityName) queryParams.set('entityName', params.entityName);
    if (params?.from) queryParams.set('from', params.from);
    if (params?.to) queryParams.set('to', params.to);
    if (params?.status) queryParams.set('status', params.status);
    return `${baseUrl}/dlq/export?${queryParams.toString()}`;
  },
};
