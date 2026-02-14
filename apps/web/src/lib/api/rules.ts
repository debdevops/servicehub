import { apiClient } from './client';

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

export interface CreateRuleRequest {
  name: string;
  description?: string;
  enabled: boolean;
  conditions: RuleCondition[];
  action: RuleAction;
  maxReplaysPerHour: number;
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
};
