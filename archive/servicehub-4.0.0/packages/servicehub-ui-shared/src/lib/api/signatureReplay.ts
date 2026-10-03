import { apiClient } from './client';
import { riskIntent, withRiskIntent } from './intentHeaders';
import type { BulkOperationJob, BulkOperationPreview, PaginatedBulkOperationJobs } from './bulkOperations';

// ─── Types ─────────────────────────────────────────────────────────

/**
 * The four replay scopes the Signature Details page exposes. Translated to a
 * `{ status, from, to }` filter server-side — the same filter vocabulary Bulk Operations
 * already uses, just resolved against a signature's current member messages instead of a
 * namespace-wide query.
 */
export type SignatureReplayScope = 'all' | 'dateRange' | 'unresolved' | 'failedReplay';

export interface SignatureReplayFilter {
  scope: SignatureReplayScope;
  /** Required (and only used) when scope is 'dateRange'. */
  from?: string;
  to?: string;
}

interface SignatureReplayRequestBody {
  status: string | null;
  from: string | null;
  to: string | null;
}

function toRequestBody(filter: SignatureReplayFilter): SignatureReplayRequestBody {
  switch (filter.scope) {
    case 'unresolved':
      return { status: 'Active', from: null, to: null };
    case 'failedReplay':
      return { status: 'ReplayFailed', from: null, to: null };
    case 'dateRange':
      return { status: null, from: filter.from ?? null, to: filter.to ?? null };
    case 'all':
    default:
      return { status: null, from: null, to: null };
  }
}

// ─── API ────────────────────────────────────────────────────────────

export const signatureReplayApi = {
  /** POST /api/v1/namespaces/{namespaceId}/dlq/signatures/{signatureHash}/replay/preview */
  preview: async (
    namespaceId: string,
    signatureHash: string,
    filter: SignatureReplayFilter,
  ): Promise<BulkOperationPreview> => {
    const response = await apiClient.post<BulkOperationPreview>(
      `/namespaces/${namespaceId}/dlq/signatures/${signatureHash}/replay/preview`,
      toRequestBody(filter),
    );
    return response.data;
  },

  /** POST /api/v1/namespaces/{namespaceId}/dlq/signatures/{signatureHash}/replay */
  start: async (
    namespaceId: string,
    signatureHash: string,
    filter: SignatureReplayFilter,
  ): Promise<BulkOperationJob> => {
    const response = await apiClient.post<BulkOperationJob>(
      `/namespaces/${namespaceId}/dlq/signatures/${signatureHash}/replay`,
      toRequestBody(filter),
      { headers: withRiskIntent(riskIntent.signatureReplay) },
    );
    return response.data;
  },

  /** GET /api/v1/signature-replay-jobs/{id} */
  getJob: async (id: string): Promise<BulkOperationJob> => {
    const response = await apiClient.get<BulkOperationJob>(`/signature-replay-jobs/${id}`);
    return response.data;
  },

  /** POST /api/v1/signature-replay-jobs/{id}/cancel */
  cancelJob: async (id: string): Promise<BulkOperationJob> => {
    const response = await apiClient.post<BulkOperationJob>(`/signature-replay-jobs/${id}/cancel`);
    return response.data;
  },

  /**
   * GET /api/v1/namespaces/{namespaceId}/dlq/signatures/{signatureHash}/replay/history
   * Past and in-flight replay jobs for one failure signature, most recent first — the Replay
   * Safety & History panel's data source.
   */
  history: async (
    namespaceId: string,
    signatureHash: string,
    page = 1,
    pageSize = 20,
  ): Promise<PaginatedBulkOperationJobs> => {
    const response = await apiClient.get<PaginatedBulkOperationJobs>(
      `/namespaces/${namespaceId}/dlq/signatures/${signatureHash}/replay/history`,
      { params: { page, pageSize } },
    );
    return response.data;
  },
};
