import { apiClient } from './client';

// ─── Types ─────────────────────────────────────────────────────────────────
//
// Mirrors the backend Playbook Ledger response DTOs
// (ServiceHub.Core.DTOs.Responses.PlaybookResponses.cs) exactly — string enum members are
// PascalCase, matching the backend's `.ToString()` serialization, not camelCase.

export type PillarKind = 'Recover' | 'Investigate' | 'Correlate' | 'Prevent';

export type PlaybookEntryState =
  | 'Proposed'
  | 'UnderReview'
  | 'Approved'
  | 'Edited'
  | 'Rejected'
  | 'Expired'
  | 'Superseded'
  | 'Revoked';

export type PlaybookDisposition = 'Approved' | 'Rejected';

export type PlaybookActorKind = 'System' | 'User' | 'ReasoningAgent';

export interface PlaybookEntry {
  id: string;
  pillarKind: PillarKind;
  proposalKind: string;
  evidenceRefJson: string;
  proposalJson: string;
  proposedAt: string;
  proposerIdentity: string;
  proposerKind: PlaybookActorKind;
  signatureHashSnapshot: string | null;
  namespaceId: string | null;
  namespaceNameSnapshot: string | null;
  providerSnapshot: string | null;
  environmentSnapshot: string | null;
  relatedRecoveryOperationId: string | null;
  expiresAt: string;
  state: PlaybookEntryState;
  disposition: PlaybookDisposition | null;
  closedAt: string | null;
}

export interface PlaybookEvent {
  id: string;
  seq: number;
  entryId: string;
  eventType: string;
  occurredAt: string;
  actorIdentity: string;
  actorKind: PlaybookActorKind;
  detailJson: string | null;
  prevHash: string;
  entryHash: string;
  schemaVersion: number;
}

export interface PlaybookEntryDetail {
  entry: PlaybookEntry;
  events: PlaybookEvent[];
}

export interface ChainVerificationResult {
  ownerId: string;
  isValid: boolean;
  eventsChecked: number;
  firstDivergentSeq: number | null;
  reason: string | null;
}

export interface PlaybookEntriesParams {
  pillarKind?: PillarKind;
  namespaceId?: string;
  state?: PlaybookEntryState;
  limit?: number;
}

/**
 * Correlation accountability (roadmap §5.D C4, §11 item 17): how many correlation hypotheses
 * (C1 same-provider, C2 cross-cloud) ServiceHub has proposed and what humans decided about them.
 * `approvalRate` is null until at least one hypothesis has reached a terminal disposition — an
 * honest "not enough evidence yet" rather than a fabricated 0%.
 */
export interface CorrelationAccountabilityReport {
  generatedAt: string;
  totalHypotheses: number;
  proposedCount: number;
  underReviewCount: number;
  approvedCount: number;
  rejectedCount: number;
  expiredCount: number;
  supersededCount: number;
  approvalRate: number | null;
}

/**
 * Counterfactual backtesting (roadmap §11 item 14): whether dispositioned anomaly-flag (I3) and
 * drift-finding (P2) proposals were followed by real recovery activity for the same entity.
 * `corroborationRate` is null until at least one proposal has been backtested — an honest "not
 * enough evidence yet" rather than a fabricated 0%.
 */
export interface BacktestEntryResult {
  playbookEntryId: string;
  pillarKind: PillarKind;
  proposalKind: string;
  entityName: string;
  namespaceId: string | null;
  proposedAt: string;
  disposition: string;
  subsequentRecoveryAttempts: number;
  subsequentRecoveredCount: number;
  subsequentReturnedCount: number;
  corroborated: boolean;
}

export interface BacktestReport {
  generatedAt: string;
  totalBacktested: number;
  corroboratedCount: number;
  corroborationRate: number | null;
  entries: BacktestEntryResult[];
}

export interface BacktestParams {
  pillarKind?: PillarKind;
  limit?: number;
}

/** One-line meaning per lifecycle state, for a tooltip/help affordance next to the state badge. */
export const PLAYBOOK_STATE_EXPLANATIONS: Record<PlaybookEntryState, string> = {
  Proposed: 'A detection worker raised this and no one has looked at it yet.',
  UnderReview: 'An operator has marked this as being looked at.',
  Approved: 'A human agreed this proposal was sound. This never itself triggers a replay or purge.',
  Edited: "The proposal's parameters were revised before a decision was made.",
  Rejected: 'A human decided this proposal was not worth acting on.',
  Expired: 'No human decision was made before the proposal expired.',
  Superseded: 'A later proposal replaced this one for the same subject.',
  Revoked: 'An operator turned off this standing rule (P5 PreventionRule only).',
};

// ─── API Client ─────────────────────────────────────────────────────────────

export const playbookApi = {
  getEntries: async (params: PlaybookEntriesParams = {}): Promise<PlaybookEntry[]> => {
    const response = await apiClient.get<PlaybookEntry[]>('/playbook/entries', { params });
    return response.data;
  },

  getEntryById: async (id: string): Promise<PlaybookEntryDetail> => {
    const response = await apiClient.get<PlaybookEntryDetail>(`/playbook/entries/${id}`);
    return response.data;
  },

  markUnderReview: async (id: string): Promise<PlaybookEntry> => {
    const response = await apiClient.post<PlaybookEntry>(`/playbook/entries/${id}/review`);
    return response.data;
  },

  disposition: async (id: string, disposition: PlaybookDisposition, reason?: string): Promise<PlaybookEntry> => {
    const response = await apiClient.post<PlaybookEntry>(`/playbook/entries/${id}/disposition`, {
      disposition,
      reason,
    });
    return response.data;
  },

  verifyChain: async (): Promise<ChainVerificationResult> => {
    const response = await apiClient.get<ChainVerificationResult>('/playbook/verify');
    return response.data;
  },

  getCorrelationAccountability: async (): Promise<CorrelationAccountabilityReport> => {
    const response = await apiClient.get<CorrelationAccountabilityReport>('/playbook/correlation-accountability');
    return response.data;
  },

  getBacktest: async (params: BacktestParams = {}): Promise<BacktestReport> => {
    const response = await apiClient.get<BacktestReport>('/playbook/backtest', { params });
    return response.data;
  },
};
