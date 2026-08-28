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
  entryCount: number;
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
  | 'Expired'
  | 'Declined';

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

/**
 * Mirrors ServiceHub.Core.DTOs.Responses.ApprovalQueueEntryResponse — one auto-replay rule match
 * the Eligibility Gate escalated for manual review, still `Active` and so still approvable.
 * Carries everything `messagesApi.replay` needs: approving an entry is a plain call to that
 * already-gated endpoint using these fields, not a separate execution path.
 */
export interface ApprovalQueueEntry {
  entryId: string;
  namespaceId: string;
  namespaceName: string | null;
  provider: string | null;
  environment: string | null;
  entityName: string;
  subscriptionName: string | null;
  sequenceNumber: number;
  failureCategory: string | null;
  ruleId: number;
  ruleName: string;
  reasonCode: string | null;
  matchedCount: number | null;
  declinedAt: string;
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

/** Mirrors ServiceHub.Core.Interfaces.AutonomyLevelCount (fleet-wide autonomy dashboard). */
export interface AutonomyLevelCount {
  actionKind: string;
  level: number;
  levelLabel: string;
  count: number;
}

/** Mirrors ServiceHub.Core.Interfaces.AutonomyGrantSummary. */
export interface AutonomyGrantSummary {
  signatureHash: string;
  actionKind: string;
  currentLevel: number;
  levelLabel: string;
  updatedAtUtc: string;
}

/** Mirrors ServiceHub.Core.Interfaces.CircuitBreakerTrip. */
export interface CircuitBreakerTrip {
  ruleId: number;
  ruleName: string;
  disabledReasonDetail: string | null;
}

/** Mirrors ServiceHub.Core.Interfaces.AutonomyTransitionSummary. */
export interface AutonomyTransitionSummary {
  signatureHash: string;
  actionKind: string;
  previousLevel: number;
  newLevel: number;
  reason: string;
  occurredAtUtc: string;
}

/**
 * Mirrors ServiceHub.Core.Interfaces.AutonomyDashboardOverview — the fleet-wide "how much
 * unattended trust has the fleet actually earned, and is anything currently constraining it?"
 * snapshot (roadmap §11 item 5, §15 item 9). Pure read-side aggregation; never itself a trust
 * decision.
 */
export interface AutonomyDashboardOverview {
  generatedAt: string;
  emergencyStopActive: boolean;
  totalSignatures: number;
  levelCounts: AutonomyLevelCount[];
  grants: AutonomyGrantSummary[];
  circuitBreakerTrips: CircuitBreakerTrip[];
  recentTransitions: AutonomyTransitionSummary[];
}

// The verification-limitation sentence every surface rendering a verification result must show
// verbatim (roadmap §13.4) — ServiceHub observes the queue, never the consumer.
export const RECOVERY_LIMITATION_SENTENCE =
  'ServiceHub observes the queue, not your consumer. This does not confirm the business transaction completed.';

// Humanized sentences for the reason codes RecoveryVerificationWorker.DetermineCoverageAsync
// persists into a closing event's DetailJson (services/api ...RecoveryVerificationWorker.cs).
// The AWS/GCP absence-proof sentences are reused verbatim from
// RecoveryEvidenceExporter.BuildLimitations so the UI and the exported manifest agree
// word-for-word.
export const RECOVERY_UNVERIFIED_REASON_LABELS: Record<string, string> = {
  AWS_NO_ABSENCE_PROOF:
    'AWS SQS has no non-destructive peek; ServiceHub cannot prove a message did not return.',
  GCP_NO_ABSENCE_PROOF:
    'GCP Pub/Sub scanning is capped per cycle and reconciliation is skipped once the cap is hit; ServiceHub cannot prove a message did not return.',
  NAMESPACE_DEREGISTERED:
    'The namespace this message belonged to has since been removed from ServiceHub, so its dead-letter queue could no longer be checked.',
  NAMESPACE_UNKNOWN:
    'This entry has no namespace recorded, so ServiceHub could not determine which provider to check.',
  AWS_PROVIDER_NOT_REGISTERED:
    'The AWS provider is not enabled on this server, so ServiceHub could not check the SQS dead-letter queue.',
  GCP_PROVIDER_NOT_REGISTERED:
    'The GCP provider is not enabled on this server, so ServiceHub could not check the Pub/Sub dead-letter queue.',
};

/**
 * Parses a RecoveryEvent.detailJson (`{"reason": "..."}`) and returns a human sentence for its
 * reason code. An unrecognized code is returned verbatim rather than dropped — "say so honestly"
 * takes priority over a nicer-sounding label ServiceHub has no basis for. Returns null when
 * there is genuinely no reason recorded, which is itself the honest answer.
 */
export function describeRecoveryDetailReason(detailJson: string | null | undefined): string | null {
  if (!detailJson) return null;
  try {
    const parsed = JSON.parse(detailJson) as { reason?: unknown };
    if (typeof parsed.reason !== 'string' || !parsed.reason) return null;
    return RECOVERY_UNVERIFIED_REASON_LABELS[parsed.reason] ?? parsed.reason;
  } catch {
    return null;
  }
}

/**
 * Operator-facing explanation for every RecoveryEntryState (roadmap §13.4 / Recovery Evidence
 * UX): what happened, why ServiceHub knows it, what it still cannot prove, and what to do next.
 * Grounded in the lifecycle documented in docs/RECOVERY-EVIDENCE.md §2 — no state invented here.
 */
export interface RecoveryStateExplanation {
  /** Short, one-line summary suitable for a tooltip. */
  summary: string;
  whatHappened: string;
  whyKnown: string;
  cannotProve: string;
  nextStep: string;
}

export const RECOVERY_STATE_EXPLANATIONS: Record<RecoveryEntryState, RecoveryStateExplanation> = {
  Executing: {
    summary: 'In flight — waiting for the provider to respond.',
    whatHappened: 'ServiceHub has asked the provider to replay or purge this message and is waiting for its response.',
    whyKnown: 'The outgoing call was recorded when it was made; no response has been recorded yet.',
    cannotProve: 'Whether the call will ultimately succeed.',
    nextStep: 'Wait — this should resolve to Observing, ExecutionFailed, or ExecutionUnknown shortly.',
  },
  Observing: {
    summary: 'Replay accepted — watching the dead-letter queue for a recurrence.',
    whatHappened: 'The provider accepted the replay, and ServiceHub is watching the dead-letter queue for the observation window to see if the message returns.',
    whyKnown: 'ServiceHub scans the DLQ during the window and matches on the recovery marker (exact) or the message body hash (heuristic).',
    cannotProve: 'Whether the message has been processed successfully downstream.',
    nextStep: 'Wait for the window to close, or write off the entry if it looks stuck.',
  },
  ExecutionFailed: {
    summary: 'The provider rejected the call outright.',
    whatHappened: 'The provider rejected the replay or purge call.',
    whyKnown: "The provider's error response was recorded on the ledger event.",
    cannotProve: 'Whether a retry would succeed.',
    nextStep: 'Check the recorded provider error and retry manually if appropriate.',
  },
  ExecutionUnknown: {
    summary: 'Outcome genuinely unknown — the process died mid-call.',
    whatHappened: "ServiceHub's process died or lost connectivity mid-call, so it never received the provider's response.",
    whyKnown: 'The outgoing request was recorded, but no response event ever followed it.',
    cannotProve: 'Whether the provider actually performed the action.',
    nextStep: 'Check the provider directly, then write off the entry once you know what happened.',
  },
  Recovered: {
    summary: 'Did not return within the observation window.',
    whatHappened: 'The replayed message did not reappear in the dead-letter queue for the full observation window.',
    whyKnown: 'ServiceHub had continuous, uncapped scan coverage of the window and found no recurrence.',
    cannotProve: 'Whether the downstream business transaction actually completed — ServiceHub only observes the queue.',
    nextStep: 'No action needed; treat this as resolved.',
  },
  Returned: {
    summary: 'Reappeared in the dead-letter queue.',
    whatHappened: 'The replayed message reappeared in the dead-letter queue within the observation window.',
    whyKnown: 'ServiceHub matched the recovery marker (exact) or the message body hash (heuristic) on a later DLQ scan.',
    cannotProve: 'The root cause of the recurrence.',
    nextStep: 'Investigate why the message failed again before replaying it further.',
  },
  Discarded: {
    summary: 'Purged deliberately — not replayed.',
    whatHappened: 'This message was purged (permanently deleted) rather than replayed, as a deliberate destructive action.',
    whyKnown: "ServiceHub recorded the purge request and the provider's acceptance.",
    cannotProve: 'Nothing further — the message is gone by design.',
    nextStep: 'No action needed; this was intentional.',
  },
  Unverified: {
    summary: 'Window closed without adequate coverage.',
    whatHappened: 'The observation window closed, but ServiceHub could not confirm one way or the other whether the message returned.',
    whyKnown: 'The recorded reason (shown alongside this state, when available) names the specific coverage gap.',
    cannotProve: 'Whether the message returned to the dead-letter queue during the window.',
    nextStep: 'Treat as unresolved — check the recorded reason and consider checking the provider directly.',
  },
  WrittenOff: {
    summary: 'Manually declared unrecoverable.',
    whatHappened: 'An operator manually declared this entry unrecoverable.',
    whyKnown: 'A mandatory reason was required and recorded at write-off time.',
    cannotProve: "The operator's stated reason is asserted, not independently verified by ServiceHub.",
    nextStep: 'No further automated action; review the recorded reason if needed.',
  },
  Expired: {
    summary: 'Aged past the tracking threshold.',
    whatHappened: 'This entry aged past the configured threshold while still unresolved.',
    whyKnown: 'An ageing sweep flagged it before it was marked expired.',
    cannotProve: 'What happened to the message after ServiceHub stopped tracking it.',
    nextStep: 'Investigate manually if this message still matters.',
  },
  Declined: {
    summary: 'Blocked before any provider call was made.',
    whatHappened: 'An eligibility check blocked this attempt before ServiceHub ever contacted the provider — for example, a recurrence-lineage cap or an autonomy-grant check.',
    whyKnown: 'The eligibility verdict and its reason were recorded at decision time.',
    cannotProve: 'Nothing was attempted, so there is nothing to observe — this is terminal from the start.',
    nextStep: 'Check the recorded reason; a human can still replay the underlying message manually if appropriate.',
  },
};

// Humanized sentences for the Eligibility Gate's Escalate-verdict reason codes
// (services/api ...RecoveryEligibilityGate.cs) — the only reason codes an Approval Queue entry
// can carry (Deny-verdict codes are excluded server-side; see ApprovalQueueService).
export const APPROVAL_QUEUE_REASON_LABELS: Record<string, string> = {
  EMERGENCY_STOP_ACTIVE: 'Blocked by an active emergency stop for this owner.',
  EMERGENCY_STOP_QUERY_ERROR: 'The emergency-stop check itself failed, so the attempt was blocked to fail closed.',
  RECURRENCE_CAP_EXCEEDED: 'This message has recurred past the automatic-replay cap on this lineage.',
  RECURRENCE_CAP_EXCEEDED_HEURISTIC: 'This message has recurred past the automatic-replay cap (heuristic match).',
  RECURRENCE_CAP_AMBIGUOUS_COLLISION: 'Multiple failure signatures matched this lineage — the cap fired on an ambiguous match.',
  RECURRENCE_CAP_QUERY_ERROR: 'The recurrence-lineage check itself failed, so the attempt was blocked to fail closed.',
  AUTONOMY_SIGNATURE_HASH_MISSING: 'No failure signature could be computed for this message, so unattended replay was refused.',
  AUTONOMY_GRANT_QUERY_ERROR: 'The autonomy-grant check itself failed, so the attempt was blocked to fail closed.',
  AUTONOMY_GRANT_INSUFFICIENT: 'This failure signature has not yet earned unattended (Standing/Unattended) trust.',
  PROVIDER_CANNOT_VERIFY_ABSENCE: "This message's cloud provider cannot independently verify DLQ absence, so unattended replay was refused.",
};

/**
 * Human sentence for an Approval Queue entry's reason code. An unrecognized code is returned
 * verbatim rather than dropped — same "say so honestly" rule as {@link describeRecoveryDetailReason}.
 */
export function describeApprovalQueueReason(reasonCode: string | null): string | null {
  if (!reasonCode) return null;
  return APPROVAL_QUEUE_REASON_LABELS[reasonCode] ?? reasonCode;
}

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

  getApprovalQueue: async (namespaceId?: string, limit = 100): Promise<ApprovalQueueEntry[]> => {
    const response = await apiClient.get<ApprovalQueueEntry[]>('/recovery/approval-queue', {
      params: { namespaceId, limit },
    });
    return response.data;
  },

  getAutonomyStatus: async (signatureHash: string): Promise<SignatureAutonomyStatus> => {
    const response = await apiClient.get<SignatureAutonomyStatus>(`/recovery/autonomy/${signatureHash}`);
    return response.data;
  },

  getAutonomyDashboard: async (): Promise<AutonomyDashboardOverview> => {
    const response = await apiClient.get<AutonomyDashboardOverview>('/recovery/autonomy-dashboard');
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
