import { apiClient } from './client';
import type { DlqHistoryItem, DlqTimelineEvent } from './dlqHistory';

// ─── Types ─────────────────────────────────────────────────────────

export interface FailureKnowledge {
  rootCause: string | null;
  resolutionNotes: string | null;
  operationalNotes: string | null;
  runbookLink: string | null;
  owner: string | null;
  replayGuidance: string | null;
  lastUpdatedAt: string | null;
  knowledgeVersion: number;
  reviewDueAt: string | null;
  tags: string | null;
}

export interface DlqClusterSignature {
  size: number;
  messageIds: number[];
  dominantEntity: string;
  dominantDeadletterReason: string;
  dominantDeadletterReasonCount: number;
  topTerms: string[];
  isNew: boolean;
  firstSeenAt: string;
  occurrenceCount: number;
  windowStart: string;
  windowEnd: string;
  explanation: string;
  knowledge?: FailureKnowledge | null;
  signatureHash: string;
  status: string;
  trend: string;
}

export interface DlqSingletonSignature {
  messageId: number;
  dominantEntity: string;
  dominantDeadletterReason: string;
}

export interface DlqSignaturesResponse {
  available: boolean;
  method: string | null;
  batchSize: number;
  clusters: DlqClusterSignature[];
  singletons: DlqSingletonSignature[];
}

/** Full detail for a single failure signature: everything on DlqClusterSignature plus its related messages. */
export interface DlqSignatureDetail extends DlqClusterSignature {
  namespaceId: string;
  confidence: string;
  isCurrentlyClustered: boolean;
  relatedMessages: DlqHistoryItem[];
}

export interface SignatureTimelineResponse {
  signatureHash: string;
  events: DlqTimelineEvent[];
}

/** Failure signature lifecycle status. */
export type SignatureLifecycleStatus = 'Active' | 'Resolved' | 'Suppressed' | 'Archived' | 'Reopened';

/** Lifecycle actions a user can take on a failure signature. */
export type SignatureLifecycleAction = 'Resolved' | 'Reopened' | 'Suppressed' | 'Archived';

export interface SignatureLifecycleStatusResponse {
  signatureHash: string;
  status: string;
  previousStatus: string | null;
  transitionedAt: string | null;
  notes: string | null;
}

// ─── API Client ────────────────────────────────────────────────────

export const dlqSignaturesApi = {
  /**
   * Get a namespace's DLQ error-cluster signatures (identity, history, explanation).
   * `available: false` means the AI service could not be reached — a normal state, not an error.
   */
  getSignatures: async (namespaceId: string): Promise<DlqSignaturesResponse> => {
    const response = await apiClient.get<DlqSignaturesResponse>(
      `/namespaces/${namespaceId}/dlq/signatures`
    );
    return response.data;
  },

  /**
   * Get full detail for a single failure signature, including related messages.
   */
  getSignatureDetail: async (namespaceId: string, signatureHash: string): Promise<DlqSignatureDetail> => {
    const response = await apiClient.get<DlqSignatureDetail>(
      `/namespaces/${namespaceId}/dlq/signatures/${signatureHash}`
    );
    return response.data;
  },

  /**
   * Get the merged, computed lifecycle timeline for a failure signature.
   */
  getSignatureTimeline: async (namespaceId: string, signatureHash: string): Promise<SignatureTimelineResponse> => {
    const response = await apiClient.get<SignatureTimelineResponse>(
      `/namespaces/${namespaceId}/dlq/signatures/${signatureHash}/timeline`
    );
    return response.data;
  },

  /**
   * Transition a failure signature's lifecycle status.
   */
  updateSignatureStatus: async (
    namespaceId: string,
    signatureHash: string,
    status: SignatureLifecycleAction,
    notes?: string
  ): Promise<SignatureLifecycleStatusResponse> => {
    const response = await apiClient.post<SignatureLifecycleStatusResponse>(
      `/namespaces/${namespaceId}/dlq/signatures/${signatureHash}/status`,
      { status, notes }
    );
    return response.data;
  },
};
