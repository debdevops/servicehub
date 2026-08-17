import { apiClient } from './client';

// ─── Types ─────────────────────────────────────────────────────────────────
//
// Mirrors the backend Recovery Evidence Ledger response DTOs
// (ServiceHub.Core.DTOs.Responses.RecoveryResponses.cs) exactly — string enum members are
// PascalCase, matching the backend's `.ToString()` serialization, not camelCase.

export interface RecoveryOperation {
  id: string;
  kind: 'Replay' | 'Purge';
  trigger: 'Manual' | 'BulkJob' | 'SignatureJob' | 'RuleReplayAll' | 'AutoRule' | 'StartupRecovery';
  actorIdentity: string;
  actorKind: 'User' | 'ApiKey' | 'Automation' | 'System';
  reason: string | null;
  namespaceId: string | null;
  namespaceNameSnapshot: string | null;
  providerSnapshot: string | null;
  environmentSnapshot: string | null;
  scopeDescription: string;
  sourceRuleId: number | null;
  sourceJobId: number | null;
  serviceVersion: string;
  openedAt: string;
  targetCount: number;
}

export type RecoveryEntryState =
  | 'Executing'
  | 'Observing'
  | 'ExecutionFailed'
  | 'ExecutionUnknown'
  | 'Recovered'
  | 'Returned'
  | 'Discarded'
  | 'Unverified'
  | 'WrittenOff'
  | 'Expired';

export interface RecoveryLedgerEntry {
  id: string;
  operationId: string;
  dlqMessageId: number | null;
  namespaceId: string | null;
  namespaceNameSnapshot: string | null;
  providerSnapshot: string | null;
  environmentSnapshot: string | null;
  entityNameSnapshot: string | null;
  entityTypeSnapshot: string | null;
  topicNameSnapshot: string | null;
  bodyHash: string;
  failureCategorySnapshot: string | null;
  deadLetterReasonSnapshot: string | null;
  signatureHashSnapshot: string | null;
  targetEntity: string;
  begunAt: string;
  markerApplied: boolean;
  state: RecoveryEntryState;
  disposition: string | null;
  verificationResult: 'NotApplicable' | 'Recovered' | 'Returned' | 'Unverified' | null;
  verificationConfidence: 'Exact' | 'Heuristic' | null;
  observationWindowEndsAt: string | null;
  closedAt: string | null;
}

export interface RecoveryEvent {
  id: string;
  ownerId: string;
  seq: number;
  entryId: string | null;
  operationId: string;
  eventType: string;
  occurredAt: string;
  actorIdentity: string;
  actorKind: string;
  detailJson: string | null;
  prevHash: string;
  entryHash: string;
  schemaVersion: number;
}

export interface RecoveryOperationDetail {
  operation: RecoveryOperation;
  entries: RecoveryLedgerEntry[];
  events: RecoveryEvent[];
}

export interface ChainVerificationResult {
  ownerId: string;
  isValid: boolean;
  eventsChecked: number;
  firstDivergentSeq: number | null;
  reason: string | null;
}

export interface RecoveryEntriesParams {
  operationId?: string;
  namespaceId?: string;
  dlqMessageId?: number;
  limit?: number;
}

/** Mirrors ServiceHub.Core.DTOs.Responses.SignatureAutonomyStatusResponse. */
export interface SignatureAutonomyStatus {
  signatureHash: string;
  actionKind: string;
  currentLevel: number;
  levelLabel: string;
  canAutoReplay: boolean;
  canProveDlqAbsence: boolean;
  blockedReason: string | null;
}

// The verification-limitation sentence every surface rendering a verification result must show
// verbatim (roadmap §13.4) — ServiceHub observes the queue, never the consumer.
export const RECOVERY_LIMITATION_SENTENCE =
  'ServiceHub observes the queue, not your consumer. This does not confirm the business transaction completed.';

// ─── API Client ─────────────────────────────────────────────────────────────

export const recoveryApi = {
  getOperations: async (namespaceId?: string, limit = 100): Promise<RecoveryOperation[]> => {
    const response = await apiClient.get<RecoveryOperation[]>('/recovery/operations', {
      params: { namespaceId, limit },
    });
    return response.data;
  },

  getOperationById: async (id: string): Promise<RecoveryOperationDetail> => {
    const response = await apiClient.get<RecoveryOperationDetail>(`/recovery/operations/${id}`);
    return response.data;
  },

  getEntries: async (params: RecoveryEntriesParams): Promise<RecoveryLedgerEntry[]> => {
    const response = await apiClient.get<RecoveryLedgerEntry[]>('/recovery/entries', { params });
    return response.data;
  },

  getAgeing: async (): Promise<RecoveryLedgerEntry[]> => {
    const response = await apiClient.get<RecoveryLedgerEntry[]>('/recovery/ageing');
    return response.data;
  },

  getAutonomyStatus: async (signatureHash: string): Promise<SignatureAutonomyStatus> => {
    const response = await apiClient.get<SignatureAutonomyStatus>(`/recovery/autonomy/${signatureHash}`);
    return response.data;
  },

  verifyChain: async (operationId: string): Promise<ChainVerificationResult> => {
    const response = await apiClient.post<ChainVerificationResult>(
      `/recovery/operations/${operationId}/verify`,
    );
    return response.data;
  },

  writeOff: async (entryId: string, reason: string): Promise<RecoveryLedgerEntry> => {
    const response = await apiClient.post<RecoveryLedgerEntry>(
      `/recovery/entries/${entryId}/write-off`,
      { reason },
      {
        headers: {
          'X-ServiceHub-Intent': 'recovery:write-off',
          'X-ServiceHub-Confirm': 'true',
        },
      },
    );
    return response.data;
  },

  /**
   * Downloads an operation's evidence export. Uses the axios client so SPA auth headers are
   * included, mirroring `auditApi.downloadExport`.
   */
  downloadExport: async (operationId: string, format: 'json' | 'csv' | 'package'): Promise<void> => {
    const response = await apiClient.get(`/recovery/operations/${operationId}/export`, {
      params: { format },
      responseType: 'blob',
    });

    const mimeType = format === 'csv' ? 'text/csv' : format === 'package' ? 'application/zip' : 'application/json';
    const extension = format === 'package' ? 'zip' : format;
    const blob = new Blob([response.data as BlobPart], { type: mimeType });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = `recovery-evidence-${operationId}.${extension}`;
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);
    setTimeout(() => URL.revokeObjectURL(url), 0);
  },
};
