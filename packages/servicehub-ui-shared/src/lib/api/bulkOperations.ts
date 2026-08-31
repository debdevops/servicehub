import { apiClient } from './client';
import { riskIntent, withRiskIntent } from './intentHeaders';

// ─── Types ─────────────────────────────────────────────────────────

export type BulkOperationType = 'Replay' | 'Purge';

export type BulkOperationStatus =
  | 'Pending'
  | 'Running'
  | 'Completed'
  | 'CompletedWithErrors'
  | 'Failed'
  | 'Cancelled';

/** Mirrors the backend's DLQ History filter shape exactly — no separate bulk-ops filter UI needed. */
export interface BulkOperationFilter {
  namespaceId: string;
  entityName?: string | null;
  from?: string | null;
  to?: string | null;
  status?: string | null;
  category?: string | null;
}

export interface BulkOperationPreviewItem {
  dlqMessageId: number;
  messageId: string;
  entityName: string;
  deadLetterReason: string | null;
  replaySafety: string | null;
}

export interface BulkOperationPreview {
  totalMatched: number;
  sample: BulkOperationPreviewItem[];
  canExecute: boolean;
  warnings: string[];
  unsafeReplayCount: number;
}

/**
 * Provider-agnostic bucket for why a replay/purge attempt failed — see the backend's
 * ReplayFailureReason. "NotFound" deliberately doesn't distinguish consumed/replayed/expired:
 * none of Azure/AWS/GCP's APIs report which of those actually happened.
 */
export type ReplayFailureReasonCategory =
  | 'NotFound'
  | 'AmbiguousOutcome'
  | 'Retryable'
  | 'ProviderError'
  | 'Other';

export interface BulkOperationFailureSample {
  messageId: string;
  entityName: string;
  reason: string;
  reasonCategory?: ReplayFailureReasonCategory | null;
}

export interface BulkOperationJob {
  id: string;
  operationType: BulkOperationType;
  status: BulkOperationStatus;
  namespaceId: string;
  namespaceDisplayName: string;
  entityNameFilter: string | null;
  statusFilter: string | null;
  categoryFilter: string | null;
  from: string | null;
  to: string | null;
  totalMatched: number;
  processedCount: number;
  successCount: number;
  failureCount: number;
  skippedCount: number;
  failureSample: BulkOperationFailureSample[] | null;
  errorSummary: string | null;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
  isCancellable: boolean;
  /**
   * While status is Pending: how many other jobs are ahead of this one on the shared
   * single-concurrency worker — 0 means "next up." Null/absent once Running/terminal, or for
   * operation types not served by a single-concurrency worker (older cached responses and demo
   * fixtures may also predate this field, hence optional).
   */
  queueAheadCount?: number | null;
}

export interface PaginatedBulkOperationJobs {
  items: BulkOperationJob[];
  totalCount: number;
  page: number;
  pageSize: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

/** Terminal statuses a UI should stop polling on. */
export const TERMINAL_BULK_OPERATION_STATUSES: readonly BulkOperationStatus[] = [
  'Completed',
  'CompletedWithErrors',
  'Failed',
  'Cancelled',
];

export function isTerminalBulkOperationStatus(status: BulkOperationStatus): boolean {
  return TERMINAL_BULK_OPERATION_STATUSES.includes(status);
}

// ─── API ────────────────────────────────────────────────────────────

export const bulkOperationsApi = {
  /** POST /api/v1/bulk-operations/preview */
  preview: async (
    operationType: BulkOperationType,
    filter: BulkOperationFilter,
  ): Promise<BulkOperationPreview> => {
    const response = await apiClient.post<BulkOperationPreview>('/bulk-operations/preview', {
      operationType,
      filter,
    });
    return response.data;
  },

  /** POST /api/v1/bulk-operations */
  create: async (
    operationType: BulkOperationType,
    filter: BulkOperationFilter,
  ): Promise<BulkOperationJob> => {
    const intent = operationType === 'Purge' ? riskIntent.bulkPurge : riskIntent.bulkReplay;
    const response = await apiClient.post<BulkOperationJob>(
      '/bulk-operations',
      { operationType, filter },
      { headers: withRiskIntent(intent) },
    );
    return response.data;
  },

  /** GET /api/v1/bulk-operations/{id} */
  get: async (id: string): Promise<BulkOperationJob> => {
    const response = await apiClient.get<BulkOperationJob>(`/bulk-operations/${id}`);
    return response.data;
  },

  /** GET /api/v1/bulk-operations */
  list: async (namespaceId?: string, page = 1, pageSize = 20): Promise<PaginatedBulkOperationJobs> => {
    const response = await apiClient.get<PaginatedBulkOperationJobs>('/bulk-operations', {
      params: { namespaceId, page, pageSize },
    });
    return response.data;
  },

  /** POST /api/v1/bulk-operations/{id}/cancel */
  cancel: async (id: string): Promise<BulkOperationJob> => {
    const response = await apiClient.post<BulkOperationJob>(`/bulk-operations/${id}/cancel`);
    return response.data;
  },
};
