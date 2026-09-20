import { apiClient } from './client';
import type { RecoveryLedgerEntry } from './recovery';
import type { PlaybookEntry } from './playbook';

// ─── Types ─────────────────────────────────────────────────────────────────
//
// Mirrors ServiceHub.Core.DTOs.Responses.IncidentResponses.cs exactly — reuses the same
// RecoveryLedgerEntry/PlaybookEntry shapes those ledgers' own API clients already declare,
// since the backend DTO reuses their response types rather than inventing new ones.

/**
 * Cheap counts over `recoveryEntries`/`playbookEntries` — a fold the backend has already done,
 * saving the caller from re-deriving "does this incident need a human" from the raw lists.
 */
export interface IncidentSummary {
  recoveryEntryCount: number;
  openRecoveryEntryCount: number;
  pendingDecisionCount: number;
  anomalyFlagCount: number;
  driftFindingCount: number;
  correlationHypothesisCount: number;
  preventionTriggerCount: number;
  replayPlanCount: number;
}

/**
 * The Incident read-model (roadmap W2.1) — one durable, addressable view of everything
 * ServiceHub knows about a failure signature: identity and lifecycle status, what it did about
 * it (recovery), and what it proposed or found about it (playbook).
 */
export interface IncidentDetailResponse {
  signatureHash: string;
  namespaceId: string;
  namespaceName: string | null;
  lifecycleStatus: string;
  firstSeenAt: string;
  lastSeenAt: string;
  occurrenceCount: number;
  dominantDeadletterReason: string | null;
  topTerms: string[];
  summary: IncidentSummary;
  recoveryEntries: RecoveryLedgerEntry[];
  playbookEntries: PlaybookEntry[];
}

// ─── API Client ────────────────────────────────────────────────────────────

export const incidentsApi = {
  /** GET /api/v1/namespaces/{namespaceId}/incidents/{signatureHash} */
  get: async (namespaceId: string, signatureHash: string): Promise<IncidentDetailResponse> => {
    const response = await apiClient.get<IncidentDetailResponse>(
      `/namespaces/${namespaceId}/incidents/${signatureHash}`,
    );
    return response.data;
  },
};
