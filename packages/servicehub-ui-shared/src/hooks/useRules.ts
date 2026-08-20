import { useQuery, useMutation, useQueryClient, UseQueryOptions } from '@tanstack/react-query';
import {
  rulesApi,
  type CreateRuleRequest,
  type RuleResponse,
  type TestRuleRequest,
} from '../lib/api/rules';
import { type ApiError } from '../lib/api/types';
import { useDemoContext, rejectDemoModeMutation } from '../lib/demo/DemoContext';
import { getMockRules } from '../lib/demo/mockProviders';
import toast from 'react-hot-toast';

const RULES_KEY = ['rules'] as const;
const DLQ_KEYS = ['dlq-history', 'dlq-summary'] as const;

/**
 * Hook for fetching all auto-replay rules.
 * Auto-refreshes every 10s to show live pendingMatchCount updates.
 */
export function useRules(enabledOnly?: boolean) {
  const { isDemoMode, cloudProvider } = useDemoContext();

  const options: UseQueryOptions<RuleResponse[]> =
    isDemoMode && cloudProvider
      ? {
          queryKey: [...RULES_KEY, 'demo', cloudProvider],
          queryFn: (): Promise<RuleResponse[]> => Promise.resolve(getMockRules()),
        }
      : {
          queryKey: [...RULES_KEY, { enabledOnly }],
          queryFn: () => rulesApi.getAll(enabledOnly),
          enabled: !isDemoMode,
          staleTime: 30_000,
          refetchInterval: (query) => (query.state.status === 'error' ? false : 30_000),
          refetchIntervalInBackground: false,
          retry: (failureCount, error: ApiError) => {
            if (error?.response?.status === 429) return false;
            if (error?.response?.status === 404) return false;
            if ((error?.response?.status ?? 0) >= 500) return false;
            return failureCount < 2;
          },
        };

  return useQuery(options);
}

/**
 * Hook for fetching a single rule by ID.
 */
export function useRule(id: number | null) {
  const { isDemoMode } = useDemoContext();

  return useQuery({
    queryKey: [...RULES_KEY, id],
    queryFn: () => rulesApi.getById(id!),
    enabled: !isDemoMode && id != null,
    staleTime: 10_000,
  });
}

/**
 * Hook for fetching rule templates.
 */
export function useRuleTemplates() {
  const { isDemoMode } = useDemoContext();

  return useQuery({
    queryKey: ['rule-templates'],
    queryFn: () => rulesApi.getTemplates(),
    enabled: !isDemoMode,
    staleTime: 60_000 * 5, // templates rarely change
  });
}

/**
 * Hook for creating a new rule.
 */
export function useCreateRule() {
  const qc = useQueryClient();
  const { isDemoMode } = useDemoContext();
  return useMutation({
    mutationFn: (request: CreateRuleRequest) =>
      isDemoMode ? rejectDemoModeMutation() : rulesApi.create(request),
    onSuccess: (rule) => {
      qc.invalidateQueries({ queryKey: RULES_KEY });
      toast.success(`Rule "${rule.name}" created`);
    },
    onError: (error: ApiError) => {
      const errorMessage =
        error?.response?.data?.detail ||
        error?.response?.data?.message ||
        error?.message ||
        'Failed to create rule';
      toast.error(errorMessage, { duration: 6000 });
    },
  });
}

/**
 * Hook for updating an existing rule.
 */
export function useUpdateRule() {
  const qc = useQueryClient();
  const { isDemoMode } = useDemoContext();
  return useMutation({
    mutationFn: ({ id, request }: { id: number; request: CreateRuleRequest }) =>
      isDemoMode ? rejectDemoModeMutation() : rulesApi.update(id, request),
    onSuccess: (rule) => {
      qc.invalidateQueries({ queryKey: RULES_KEY });
      toast.success(`Rule "${rule.name}" updated`);
    },
    onError: (error: ApiError) => {
      const errorMessage =
        error?.response?.data?.detail ||
        error?.response?.data?.message ||
        error?.message ||
        'Failed to update rule';
      toast.error(errorMessage, { duration: 6000 });
    },
  });
}

/**
 * Hook for deleting a rule.
 */
export function useDeleteRule() {
  const qc = useQueryClient();
  const { isDemoMode } = useDemoContext();
  return useMutation({
    mutationFn: (id: number) => (isDemoMode ? rejectDemoModeMutation() : rulesApi.delete(id)),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: RULES_KEY });
      toast.success('Rule deleted');
    },
    onError: (error: ApiError) => {
      const errorMessage =
        error?.response?.data?.detail ||
        error?.response?.data?.message ||
        error?.message ||
        'Failed to delete rule';
      toast.error(errorMessage, { duration: 6000 });
    },
  });
}

/**
 * Hook for toggling a rule's enabled state.
 */
export function useToggleRule() {
  const qc = useQueryClient();
  const { isDemoMode } = useDemoContext();
  return useMutation({
    mutationFn: (id: number) => (isDemoMode ? rejectDemoModeMutation() : rulesApi.toggle(id)),
    onSuccess: (rule) => {
      qc.invalidateQueries({ queryKey: RULES_KEY });
      toast.success(`Rule "${rule.name}" ${rule.enabled ? 'enabled' : 'disabled'}`);
    },
    onError: (error: ApiError) => {
      const errorMessage =
        error?.response?.data?.detail ||
        error?.response?.data?.message ||
        error?.message ||
        'Failed to toggle rule';
      toast.error(errorMessage, { duration: 6000 });
    },
  });
}

/**
 * Hook for testing a rule against live DLQ messages.
 */
export function useTestRule() {
  const { isDemoMode } = useDemoContext();
  return useMutation({
    mutationFn: (request: TestRuleRequest) =>
      isDemoMode ? rejectDemoModeMutation() : rulesApi.test(request),
    onError: (error: ApiError) => {
      const errorMessage =
        error?.response?.data?.detail ||
        error?.response?.data?.message ||
        error?.message ||
        'Failed to test rule';
      toast.error(errorMessage, { duration: 6000 });
    },
  });
}

/**
 * Hook for executing replay-all on a rule.
 * Replays every DLQ message that matches the rule's conditions.
 */
// Replay-all's matched messages are not scoped to one namespace in the response (a rule has no
// persisted namespace scope today — see the Auto-Replay Rule UI), so these are invalidated
// unscoped, matching the existing DLQ_KEYS approach on this same hook: broader than strictly
// necessary for a single-namespace rule, but never wrong, and correct for a cross-namespace one.
function invalidateReplayAllQueries(qc: ReturnType<typeof useQueryClient>) {
  qc.invalidateQueries({ queryKey: RULES_KEY });
  // Also invalidate DLQ data since messages have been replayed
  DLQ_KEYS.forEach((key) => qc.invalidateQueries({ queryKey: [key] }));
  // Recovery Ledger otherwise only refreshes on its own 60s refetchInterval — without this,
  // a replay-all that just recorded ledger entries can take up to a minute to show up on the
  // Recovery Ledger page.
  qc.invalidateQueries({ queryKey: ['recovery-operations'] });
  qc.invalidateQueries({ queryKey: ['recovery-entries'] });
  // Sidebar/queue-list DLQ counts and the Failure Signatures list otherwise never refresh after
  // Replay All — dlq-signatures in particular has no refetchInterval of its own at all.
  qc.invalidateQueries({ queryKey: ['queues'] });
  qc.invalidateQueries({ queryKey: ['namespace-stats'] });
  qc.invalidateQueries({ queryKey: ['dlq-signatures'] });
  qc.invalidateQueries({ queryKey: ['dlq-signature-detail'] });
  qc.invalidateQueries({ queryKey: ['fleet-overview'] });
}

export function useReplayAll() {
  const qc = useQueryClient();
  const { isDemoMode } = useDemoContext();
  return useMutation({
    mutationFn: (ruleId: number) =>
      isDemoMode ? rejectDemoModeMutation() : rulesApi.replayAll(ruleId),
    onSuccess: (result) => {
      invalidateReplayAllQueries(qc);

      if (result.replayed > 0) {
        toast.success(
          `Replayed ${result.replayed} of ${result.totalMatched} matched messages` +
            (result.failed > 0 ? ` (${result.failed} failed)` : ''),
        );
      } else if (result.totalMatched === 0) {
        toast('No messages matched this rule\'s conditions', { icon: 'ℹ️' });
      } else if (result.failed === 0 && result.skipped > 0) {
        toast(
          `${result.skipped} matched message${result.skipped === 1 ? ' was' : 's were'} skipped (stale or out-of-scope records) — nothing to replay`,
          { icon: 'ℹ️', duration: 6000 },
        );
      } else {
        toast.error(`All ${result.totalMatched} matched messages failed to replay`);
      }
    },
    onError: (error: ApiError) => {
      // A 504 does not mean the operation stopped — the backend's own error body says so
      // verbatim ("Some messages may have been replayed. Please check and retry."), and it's
      // been observed live continuing to replay past the HTTP timeout. Treating this as a plain
      // failure would tell the operator the opposite of what may have actually happened, so this
      // still refreshes every query a successful replay-all would have, rather than leaving the
      // UI showing pre-attempt counts under a "failed" toast.
      if (error?.response?.status === 504) {
        invalidateReplayAllQueries(qc);
        toast(
          'Replay-all took too long to confirm — some messages may already have been replayed. Refresh to check the current state before retrying.',
          { icon: '⏳', duration: 10000 },
        );
        return;
      }

      const errorMessage =
        error?.response?.data?.detail ||
        error?.response?.data?.message ||
        error?.message ||
        'Failed to execute replay-all';
      toast.error(errorMessage, { duration: 6000 });
    },
  });
}

/**
 * Hook for generating intelligent auto-replay rules from DLQ patterns.
 */
export function useGenerateRules() {
  const qc = useQueryClient();
  const { isDemoMode } = useDemoContext();
  return useMutation({
    mutationFn: (namespaceId?: string) =>
      isDemoMode ? rejectDemoModeMutation() : rulesApi.generateRules(namespaceId),
    onSuccess: (result) => {
      qc.invalidateQueries({ queryKey: RULES_KEY });
      if (result.rulesCreated > 0) {
        toast.success(
          `Analysed ${result.analysedMessages} messages — created ${result.rulesCreated} intelligent rules`,
        );
      } else if (result.analysedMessages === 0) {
        toast('No active DLQ messages to analyse', { icon: 'ℹ️' });
      } else {
        toast('All detected patterns already have rules', { icon: 'ℹ️' });
      }
    },
    onError: (error: ApiError) => {
      const errorMessage =
        error?.response?.data?.detail ||
        error?.response?.data?.message ||
        error?.message ||
        'Failed to generate intelligent rules';
      toast.error(errorMessage, { duration: 6000 });
    },
  });
}
