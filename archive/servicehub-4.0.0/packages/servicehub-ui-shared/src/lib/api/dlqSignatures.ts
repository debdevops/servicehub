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
  updatedBy: string | null;
  isReviewOverdue: boolean;
}

/** Request to create or update a failure signature's operational knowledge. */
export interface UpsertKnowledgeRequest {
  rootCause: string;
  resolutionNotes?: string | null;
  operationalNotes?: string | null;
  runbookLink?: string | null;
  owner?: string | null;
  replayGuidance?: string | null;
  tags?: string | null;
  reviewDueAt?: string | null;
  changedBy?: string | null;
}

/** A prior version of a failure signature's operational knowledge. */
export interface FailureKnowledgeHistoryEntry {
  knowledgeVersion: number;
  rootCause: string | null;
  resolutionNotes: string | null;
  operationalNotes: string | null;
  runbookLink: string | null;
  owner: string | null;
  replayGuidance: string | null;
  tags: string | null;
  reviewDueAt: string | null;
  updatedBy: string | null;
  updatedAt: string;
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

/**
 * The most recent signature-replay job's outcome for one namespace, if the signature has ever
 * been replayed there — a single already-durable fact, never a computed safety score.
 */
export interface LastReplayOutcome {
  status: string;
  createdAt: string;
}

/**
 * One other namespace where this exact signature hash has also been observed — same dominant
 * deadletter reason and top terms as the signature being investigated, since the hash is
 * derived from those alone. `knowledge` is null when no root cause has been recorded there.
 * `lastReplayOutcome` is null when the signature has never been replayed in that namespace.
 */
export interface RootCauseMatch {
  namespaceId: string;
  occurrenceCount: number;
  firstSeenAt: string;
  lastSeenAt: string;
  lifecycleStatus: string;
  knowledge: FailureKnowledge | null;
  lastReplayOutcome: LastReplayOutcome | null;
}

/** Fleet-wide view of one failure signature: where else it has occurred, and the total. */
export interface RootCauseExplorerResponse {
  signatureHash: string;
  dominantDeadletterReason: string;
  topTerms: string[];
  totalOccurrencesAcrossFleet: number;
  matches: RootCauseMatch[];
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
      `/namespaces/${namespaceId}/dlq/signatures/${signatureHash}`,
      {
        // SignatureDetailsPage renders its own "Signature not found" state for a 404.
        _ownErrorToast: true,
      }
    );
    return response.data;
  },

  /**
   * Get the merged, computed lifecycle timeline for a failure signature.
   */
  getSignatureTimeline: async (namespaceId: string, signatureHash: string): Promise<SignatureTimelineResponse> => {
    const response = await apiClient.get<SignatureTimelineResponse>(
      `/namespaces/${namespaceId}/dlq/signatures/${signatureHash}/timeline`,
      {
        // Fetched alongside getSignatureDetail for the same signature — a missing signature
        // 404s both requests, and without this the generic interceptor toast would fire twice.
        _ownErrorToast: true,
      }
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

  /**
   * Create or update a failure signature's operational knowledge. On update, the prior
   * version is preserved and retrievable via `getKnowledgeHistory`.
   */
  upsertKnowledge: async (
    namespaceId: string,
    signatureHash: string,
    request: UpsertKnowledgeRequest
  ): Promise<FailureKnowledge> => {
    const response = await apiClient.put<FailureKnowledge>(
      `/namespaces/${namespaceId}/dlq/signatures/${signatureHash}/knowledge`,
      request
    );
    return response.data;
  },

  /**
   * Get prior versions of a failure signature's operational knowledge, most recent first.
   */
  getKnowledgeHistory: async (
    namespaceId: string,
    signatureHash: string
  ): Promise<FailureKnowledgeHistoryEntry[]> => {
    const response = await apiClient.get<FailureKnowledgeHistoryEntry[]>(
      `/namespaces/${namespaceId}/dlq/signatures/${signatureHash}/knowledge/history`
    );
    return response.data;
  },

  /**
   * Mark a failure signature's knowledge as needing review by the given date.
   */
  markForReview: async (
    namespaceId: string,
    signatureHash: string,
    reviewDueAt: string
  ): Promise<FailureKnowledge> => {
    const response = await apiClient.post<FailureKnowledge>(
      `/namespaces/${namespaceId}/dlq/signatures/${signatureHash}/knowledge/review`,
      { reviewDueAt }
    );
    return response.data;
  },

  /**
   * Root Cause Explorer: find this signature's occurrences in every other namespace in the
   * fleet. Matching is exact signature-hash equality only — never a fuzzy/scored guess.
   */
  getRootCauseMatches: async (namespaceId: string, signatureHash: string): Promise<RootCauseExplorerResponse> => {
    const response = await apiClient.get<RootCauseExplorerResponse>(
      `/namespaces/${namespaceId}/dlq/signatures/${signatureHash}/root-cause-matches`
    );
    return response.data;
  },
};
