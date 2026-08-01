import { apiClient } from './client';

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
};
