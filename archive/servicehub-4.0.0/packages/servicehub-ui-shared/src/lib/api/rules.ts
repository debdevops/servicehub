import { apiClient } from './client';
import { riskIntent, withRiskIntent } from './intentHeaders';
import type { CloudProviderType, EnvironmentType } from './types';

// ─── Types ─────────────────────────────────────────────────────────

export interface RuleCondition {
  field: string;
  operator: string;
  value: string;
  caseSensitive?: boolean;
  propertyKey?: string;
}

export interface RuleAction {
  autoReplay: boolean;
  delaySeconds: number;
  maxRetries: number;
  exponentialBackoff: boolean;
  targetEntity?: string;
}

/**
 * Server-computed scope for a rule — resolved from `RuleResponse.namespaceId` via
 * `INamespaceRepository` on the API side, rather than inferred client-side from conditions
 * (see the retired `resolveRuleScope`/`ruleScope.ts` heuristic this type replaces).
 */
export interface RuleNamespaceScope {
  kind: 'Global' | 'Namespace' | 'Unresolved';
  /** Set only when kind === 'Namespace'. */
  name?: string | null;
  /** Set only when kind === 'Namespace'. */
  provider?: CloudProviderType | null;
  /** Set only when kind === 'Namespace'. */
  environment?: EnvironmentType | null;
}

export interface RuleResponse {
  id: number;
  name: string;
  description: string | null;
  enabled: boolean;
  conditions: RuleCondition[];
  action: RuleAction;
  createdAt: string;
  updatedAt: string | null;
  matchCount: number;
  successCount: number;
  successRate: number;
  maxReplaysPerHour: number;
  pendingMatchCount: number;
  disabledReason: 'Manual' | 'CircuitBreaker' | null;
  disabledReasonDetail: string | null;
  /** Namespace this rule is scoped to, or null for Global (matches every namespace). */
  namespaceId: string | null;
  namespaceScope: RuleNamespaceScope;
}

export interface RuleMatchResultResponse {
  messageId: number;
  serviceBusMessageId: string;
  entityName: string;
  isMatch: boolean;
  matchReason: string | null;
  deadLetterReason: string | null;
}

export interface RuleTestResponse {
  totalTested: number;
  matchedCount: number;
  estimatedSuccessRate: number;
  sampleMatches: RuleMatchResultResponse[];
}

export interface ReplayAllResponse {
  totalMatched: number;
  replayed: number;
  failed: number;
  skipped: number;
  results: ReplayAllItemResponse[];
}

export interface ReplayAllItemResponse {
  dlqRecordId: number;
  messageId: string;
  entityName: string;
  outcome: string;
  error: string | null;
}

export interface RuleTemplateResponse {
  id: string;
  name: string;
  description: string;
  category: string;
  conditions: RuleCondition[];
  action: RuleAction;
  usageCount: number;
  rating: number;
}

export interface GenerateRulesResponse {
  analysedMessages: number;
  patternsDetected: number;
  rulesCreated: number;
  rulesSkipped: number;
  rules: RuleResponse[];
}

export interface CreateRuleRequest {
  name: string;
  description?: string;
  enabled: boolean;
  conditions: RuleCondition[];
  action: RuleAction;
  maxReplaysPerHour: number;
  /** Namespace to scope this rule to, or omitted/null for Global (matches every namespace). */
  namespaceId?: string | null;
}

export interface TestRuleRequest {
  conditions?: RuleCondition[];
  ruleId?: number;
  namespaceId?: string;
  maxMessages?: number;
}

// ─── API Client ────────────────────────────────────────────────────

const BASE = '/dlq/rules';

export const rulesApi = {
  /** Get all rules, optionally only enabled ones */
  getAll: async (enabledOnly?: boolean): Promise<RuleResponse[]> => {
    const params = enabledOnly != null ? { enabledOnly } : undefined;
    const { data } = await apiClient.get<RuleResponse[]>(BASE, { params });
    return data;
  },

  /** Get a single rule by ID */
  getById: async (id: number): Promise<RuleResponse> => {
    const { data } = await apiClient.get<RuleResponse>(`${BASE}/${id}`);
    return data;
  },

  /** Create a new rule */
  create: async (request: CreateRuleRequest): Promise<RuleResponse> => {
    const { data } = await apiClient.post<RuleResponse>(BASE, request);
    return data;
  },

  /** Update an existing rule */
  update: async (id: number, request: CreateRuleRequest): Promise<RuleResponse> => {
    const { data } = await apiClient.put<RuleResponse>(`${BASE}/${id}`, request);
    return data;
  },

  /** Delete a rule */
  delete: async (id: number): Promise<void> => {
    await apiClient.delete(`${BASE}/${id}`);
  },

  /** Toggle a rule's enabled status */
  toggle: async (id: number): Promise<RuleResponse> => {
    const { data } = await apiClient.post<RuleResponse>(`${BASE}/${id}/toggle`);
    return data;
  },

  /** Execute replay-all for a rule — replays every matching DLQ message */
  replayAll: async (ruleId: number): Promise<ReplayAllResponse> => {
    // Use extended timeout — bulk replay can take time for many messages
    const { data } = await apiClient.post<ReplayAllResponse>(`${BASE}/${ruleId}/replay-all`, null, {
      headers: withRiskIntent(riskIntent.replayAllRules),
      timeout: 120_000, // 2 minutes (override default 30s)
    });
    return data;
  },

  /** Test a rule against active DLQ messages */
  test: async (request: TestRuleRequest): Promise<RuleTestResponse> => {
    const { data } = await apiClient.post<RuleTestResponse>(`${BASE}/test`, request);
    return data;
  },

  /** Get rule templates */
  getTemplates: async (): Promise<RuleTemplateResponse[]> => {
    const { data } = await apiClient.get<RuleTemplateResponse[]>(`${BASE}/templates`);
    return data;
  },

  /** Generate intelligent auto-replay rules from DLQ patterns */
  generateRules: async (namespaceId?: string): Promise<GenerateRulesResponse> => {
    const params = namespaceId ? { namespaceId } : undefined;
    const { data } = await apiClient.post<GenerateRulesResponse>(`${BASE}/generate`, null, { params });
    return data;
  },
};
