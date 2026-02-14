import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  rulesApi,
  type CreateRuleRequest,
  type TestRuleRequest,
} from '@/lib/api/rules';
import toast from 'react-hot-toast';

const RULES_KEY = ['rules'] as const;
const DLQ_KEYS = ['dlq-history', 'dlq-summary'] as const;

/**
 * Hook for fetching all auto-replay rules.
 */
export function useRules(enabledOnly?: boolean) {
  return useQuery({
    queryKey: [...RULES_KEY, { enabledOnly }],
    queryFn: () => rulesApi.getAll(enabledOnly),
    staleTime: 10_000,
    refetchInterval: 30_000,
  });
}

/**
 * Hook for fetching a single rule by ID.
 */
export function useRule(id: number | null) {
  return useQuery({
    queryKey: [...RULES_KEY, id],
    queryFn: () => rulesApi.getById(id!),
    enabled: id != null,
    staleTime: 10_000,
  });
}

/**
 * Hook for fetching rule templates.
 */
export function useRuleTemplates() {
  return useQuery({
    queryKey: ['rule-templates'],
    queryFn: () => rulesApi.getTemplates(),
    staleTime: 60_000 * 5, // templates rarely change
  });
}

/**
 * Hook for creating a new rule.
 */
export function useCreateRule() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (request: CreateRuleRequest) => rulesApi.create(request),
    onSuccess: (rule) => {
      qc.invalidateQueries({ queryKey: RULES_KEY });
      toast.success(`Rule "${rule.name}" created`);
    },
    onError: () => toast.error('Failed to create rule'),
  });
}

/**
 * Hook for updating an existing rule.
 */
export function useUpdateRule() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, request }: { id: number; request: CreateRuleRequest }) =>
      rulesApi.update(id, request),
    onSuccess: (rule) => {
      qc.invalidateQueries({ queryKey: RULES_KEY });
      toast.success(`Rule "${rule.name}" updated`);
    },
    onError: () => toast.error('Failed to update rule'),
  });
}

/**
 * Hook for deleting a rule.
 */
export function useDeleteRule() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => rulesApi.delete(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: RULES_KEY });
      toast.success('Rule deleted');
    },
    onError: () => toast.error('Failed to delete rule'),
  });
}

/**
 * Hook for toggling a rule's enabled state.
 */
export function useToggleRule() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => rulesApi.toggle(id),
    onSuccess: (rule) => {
      qc.invalidateQueries({ queryKey: RULES_KEY });
      toast.success(`Rule "${rule.name}" ${rule.enabled ? 'enabled' : 'disabled'}`);
    },
    onError: () => toast.error('Failed to toggle rule'),
  });
}

/**
 * Hook for testing a rule against live DLQ messages.
 */
export function useTestRule() {
  return useMutation({
    mutationFn: (request: TestRuleRequest) => rulesApi.test(request),
    onError: () => toast.error('Failed to test rule'),
  });
}

/**
 * Hook for executing replay-all on a rule.
 * Replays every DLQ message that matches the rule's conditions.
 */
export function useReplayAll() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (ruleId: number) => rulesApi.replayAll(ruleId),
    onSuccess: (result) => {
      qc.invalidateQueries({ queryKey: RULES_KEY });
      // Also invalidate DLQ data since messages have been replayed
      DLQ_KEYS.forEach((key) => qc.invalidateQueries({ queryKey: [key] }));

      if (result.replayed > 0) {
        toast.success(
          `Replayed ${result.replayed} of ${result.totalMatched} matched messages` +
            (result.failed > 0 ? ` (${result.failed} failed)` : ''),
        );
      } else if (result.totalMatched === 0) {
        toast('No messages matched this rule\'s conditions', { icon: 'ℹ️' });
      } else {
        toast.error(`All ${result.totalMatched} matched messages failed to replay`);
      }
    },
    onError: () => toast.error('Failed to execute replay-all'),
  });
}
