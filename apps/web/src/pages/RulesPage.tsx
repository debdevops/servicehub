import { useState, useMemo, useEffect } from 'react';
import { createPortal } from 'react-dom';
import { useQueries } from '@tanstack/react-query';
import { Plus, Zap, RefreshCw, ToggleLeft, ToggleRight, Pencil, Trash2, FlaskConical, Play, AlertTriangle, X, Shield, Brain, Globe2 } from 'lucide-react';
import { RuleBuilderDialog, TemplateGalleryDialog, RuleTestDialog } from '@/components/rules';
import { ConfirmDialog } from '@/components/ConfirmDialog';
import { HelpTooltip } from '@/components/help';
import { EnvironmentBadge } from '@/components/EnvironmentBadge';
import { tooltips } from '@servicehub/ui-shared/lib/helpContent';
import { useNamespaces } from '@servicehub/ui-shared/hooks/useNamespaces';
import { useAllNamespacesQueues } from '@servicehub/ui-shared/hooks/useQueues';
import { apiClient } from '@servicehub/ui-shared/lib/api/client';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import { findRuleEntityWarnings, type KnownEntities } from '@servicehub/ui-shared/lib/ruleValidation';
import { resolveRuleScope, type NamespaceEntityIndex, type RuleScope, type ScopedEntity } from '@servicehub/ui-shared/lib/ruleScope';
import { ProviderBadge, getProviderStyle, getProviderServiceShortName } from '@servicehub/ui-shared/lib/providerStyles';
import type { Topic } from '@servicehub/ui-shared/lib/api/types';
import { useFocusTrap } from '@servicehub/ui-shared/hooks/useFocusTrap';
import {
  useRules,
  useCreateRule,
  useUpdateRule,
  useDeleteRule,
  useToggleRule,
  useReplayAll,
  useGenerateRules,
} from '@servicehub/ui-shared/hooks/useRules';
import type {
  RuleResponse,
  RuleCondition,
  RuleAction,
  RuleTemplateResponse,
  CreateRuleRequest,
} from '@servicehub/ui-shared/lib/api/rules';

export function RulesPage() {
  const { data: rules, isLoading, isError, refetch, isFetching } = useRules();
  const { isDemoMode } = useDemoContext();

  // Entities across every connected namespace (all providers) — used to flag
  // rules whose entity references no longer exist anywhere.
  const { data: namespaces } = useNamespaces();
  const namespaceIds = useMemo(() => namespaces?.map((ns) => ns.id) ?? [], [namespaces]);
  const allQueueStats = useAllNamespacesQueues(namespaceIds, false);
  const topicResults = useQueries({
    queries: namespaceIds.map((id) => ({
      queryKey: ['topics', id] as const,
      queryFn: async (): Promise<Topic[]> => {
        const response = await apiClient.get<Topic[]>(`/namespaces/${id}/topics`, { _silent: true });
        return response.data;
      },
      enabled: !isDemoMode && !!id,
      staleTime: 60_000,
      refetchIntervalInBackground: false,
      retry: 1,
    })),
  });
  const knownEntities: KnownEntities = useMemo(
    () => ({
      queues: allQueueStats.flatMap((s) => s.queues?.map((q) => q.name) ?? []),
      topics: topicResults.flatMap((r) => r.data?.map((t) => t.name) ?? []),
      loaded:
        namespaceIds.length > 0 &&
        allQueueStats.every((s) => !s.isLoading) &&
        topicResults.every((r) => !r.isLoading),
    }),
    [allQueueStats, topicResults, namespaceIds.length],
  );

  // Same underlying data as knownEntities above, but keeping the per-namespace grouping
  // (rather than flattening into one list) so a rule's EntityName/TopicName condition can be
  // resolved back to the specific namespace(s)/cloud(s) it lives in — no extra network calls.
  const namespaceEntityIndex: NamespaceEntityIndex[] = useMemo(
    () =>
      (namespaces ?? []).map((ns) => ({
        namespaceId: ns.id,
        namespace: ns,
        queues:
          allQueueStats.find((s) => s.namespaceId === ns.id)?.queues?.map((q) => ({
            name: q.name,
            deadLetterTargetQueue: q.deadLetterTargetQueue,
          })) ?? [],
        topics: topicResults[namespaceIds.indexOf(ns.id)]?.data?.map((t) => t.name) ?? [],
      })),
    [namespaces, allQueueStats, topicResults, namespaceIds],
  );
  const createMutation = useCreateRule();
  const updateMutation = useUpdateRule();
  const deleteMutation = useDeleteRule();
  const toggleMutation = useToggleRule();
  const replayAllMutation = useReplayAll();
  const generateMutation = useGenerateRules();

  // Dialog state
  const [showBuilder, setShowBuilder] = useState(false);
  const [showTemplates, setShowTemplates] = useState(false);
  const [showTest, setShowTest] = useState(false);
  const [editRule, setEditRule] = useState<RuleResponse | null>(null);
  const [testRule, setTestRule] = useState<RuleResponse | null>(null);
  const [replayAllRule, setReplayAllRule] = useState<RuleResponse | null>(null);
  const [deleteRule, setDeleteRule] = useState<RuleResponse | null>(null);
  const [templatePrefill, setTemplatePrefill] = useState<{
    conditions: RuleCondition[];
    action: RuleAction;
  } | null>(null);

  const handleCreate = () => {
    setEditRule(null);
    setTemplatePrefill(null);
    setShowBuilder(true);
  };

  const handleEdit = (rule: RuleResponse) => {
    setEditRule(rule);
    setTemplatePrefill(null);
    setShowBuilder(true);
  };

  const handleTemplateSelect = (template: RuleTemplateResponse) => {
    setShowTemplates(false);
    setEditRule(null);
    setTemplatePrefill({ conditions: template.conditions, action: template.action });
    setShowBuilder(true);
  };

  const handleTest = (rule: RuleResponse) => {
    setTestRule(rule);
    setShowTest(true);
  };

  const handleSave = (request: CreateRuleRequest) => {
    if (editRule) {
      updateMutation.mutate(
        { id: editRule.id, request },
        { onSuccess: () => setShowBuilder(false) },
      );
    } else {
      createMutation.mutate(request, { onSuccess: () => setShowBuilder(false) });
    }
  };

  const handleDelete = (rule: RuleResponse) => {
    setDeleteRule(rule);
  };

  const confirmDelete = () => {
    if (!deleteRule) return;
    deleteMutation.mutate(deleteRule.id, {
      onSettled: () => setDeleteRule(null),
    });
  };

  const handleReplayAll = (rule: RuleResponse) => {
    setReplayAllRule(rule);
  };

  const confirmReplayAll = () => {
    if (!replayAllRule) return;
    replayAllMutation.mutate(replayAllRule.id, {
      onSettled: () => setReplayAllRule(null),
    });
  };

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      {/* Header */}
      <div className="bg-white border-b border-gray-200 px-6 py-4 shrink-0">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-xl font-bold text-gray-900">
              Auto-Replay Rules
              <HelpTooltip {...tooltips.rules.ruleBuilder} position="bottom" className="ml-1.5" />
            </h1>
            <p className="text-sm text-gray-500 mt-0.5">
              Define rules that automatically replay dead-letter messages matching specific conditions
            </p>
          </div>
          <div className="flex items-center gap-2">
            <button
              onClick={() => generateMutation.mutate(undefined)}
              disabled={generateMutation.isPending}
              className="flex items-center gap-1.5 px-3 py-2 bg-amber-50 border border-amber-200 rounded-lg text-sm text-amber-700 hover:bg-amber-100 transition-colors disabled:opacity-50"
              title="Analyse DLQ patterns and automatically create smart rules"
            >
              <Brain className={`w-4 h-4 ${generateMutation.isPending ? 'animate-pulse' : ''}`} />
              {generateMutation.isPending ? 'Analysing...' : 'Generate Intelligent Rules'}
            </button>
            <button
              onClick={() => setShowTemplates(true)}
              className="flex items-center gap-1.5 px-3 py-2 border border-gray-200 rounded-lg text-sm text-gray-700 hover:bg-gray-50 transition-colors focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-1"
            >
              <Zap className="w-4 h-4 text-amber-500" />
              Browse Templates
            </button>
            <button
              onClick={handleCreate}
              className="flex items-center gap-1.5 px-3 py-2 bg-primary-700 hover:bg-primary-800 text-white rounded-lg text-sm font-medium transition-colors"
            >
              <Plus className="w-4 h-4" />
              Create Rule
            </button>
            <button
              onClick={() => refetch()}
              disabled={isFetching}
              className="p-2 border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-1"
              title="Refresh"
            >
              <RefreshCw className={`w-4 h-4 text-gray-500 ${isFetching ? 'animate-spin' : ''}`} />
            </button>
          </div>
        </div>
      </div>

      {/* Body */}
      <div className="flex-1 overflow-y-auto p-6">
        {isLoading ? (
          <div className="flex items-center justify-center gap-2 py-12 text-sm text-gray-500">
            <RefreshCw className="w-4 h-4 animate-spin" />
            Loading rules…
          </div>
        ) : isError ? (
          <div className="flex flex-col items-center justify-center gap-3 py-16 text-center">
            <AlertTriangle className="w-10 h-10 text-red-400" />
            <p className="text-gray-600 font-medium">Failed to load rules</p>
            <button
              onClick={() => refetch()}
              className="px-4 py-2 text-sm text-primary-600 hover:text-primary-700 border border-primary-300 rounded-lg hover:bg-primary-50 transition-colors focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-1"
            >
              Try Again
            </button>
          </div>
        ) : rules && rules.length > 0 ? (
          <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
            {rules.map((rule) => (
              <RuleCard
                key={rule.id}
                rule={rule}
                scope={resolveRuleScope(rule.conditions, namespaceEntityIndex, knownEntities.loaded)}
                entityWarnings={findRuleEntityWarnings(rule.conditions, rule.action, knownEntities)}
                onEdit={() => handleEdit(rule)}
                onDelete={() => handleDelete(rule)}
                onToggle={() => toggleMutation.mutate(rule.id)}
                onTest={() => handleTest(rule)}
                onReplayAll={() => handleReplayAll(rule)}
                isReplayingAll={replayAllMutation.isPending && replayAllRule?.id === rule.id}
              />
            ))}
          </div>
        ) : (
          <EmptyState
            onCreate={handleCreate}
            onBrowseTemplates={() => setShowTemplates(true)}
            onGenerateRules={() => generateMutation.mutate(undefined)}
            isGenerating={generateMutation.isPending}
          />
        )}
      </div>

      {/* Dialogs */}
      <RuleBuilderDialog
        open={showBuilder}
        onClose={() => setShowBuilder(false)}
        onSave={handleSave}
        editRule={editRule}
        initialConditions={templatePrefill?.conditions}
        initialAction={templatePrefill?.action}
        isSaving={createMutation.isPending || updateMutation.isPending}
        knownEntities={knownEntities}
      />

      <TemplateGalleryDialog
        open={showTemplates}
        onClose={() => setShowTemplates(false)}
        onSelect={handleTemplateSelect}
      />

      <RuleTestDialog
        open={showTest}
        onClose={() => { setShowTest(false); setTestRule(null); }}
        rule={testRule}
      />

      <ReplayAllConfirmDialog
        rule={replayAllRule}
        scope={
          replayAllRule
            ? resolveRuleScope(replayAllRule.conditions, namespaceEntityIndex, knownEntities.loaded)
            : null
        }
        isExecuting={replayAllMutation.isPending}
        onConfirm={confirmReplayAll}
        onCancel={() => setReplayAllRule(null)}
      />

      <ConfirmDialog
        isOpen={deleteRule !== null}
        title="Delete Rule"
        message={`Delete rule "${deleteRule?.name}"? This cannot be undone. Rules that have already matched messages will lose their history statistics.`}
        confirmLabel="Delete"
        cancelLabel="Cancel"
        variant="danger"
        onConfirm={confirmDelete}
        onCancel={() => setDeleteRule(null)}
      />
    </div>
  );
}

// ─── Sub-Components ────────────────────────────────────────────────

// Identifies exactly ONE resolved namespace/cloud/entity a rule's EntityName/TopicName
// condition matched. Four visually distinct facts, per the semantic rules this satisfies:
// Cloud (badge, never icon-only), Namespace/environment, Provider service (distinct from
// Cloud — "SQS" not "AWS SQS", since Cloud is already shown), and Entity/Topic+Subscription
// (distinct from Namespace), plus a DLQ line only when one is genuinely derivable — never
// fabricated (see buildDlqLabel in lib/ruleScope.ts for exactly what's real per provider).
function ScopeIdentity({ match }: { match: ScopedEntity }) {
  return (
    <div className="space-y-1">
      <div className="flex flex-wrap items-center gap-1.5">
        <ProviderBadge provider={match.cloudProvider} />
        <span className="text-gray-400">·</span>
        <span className="text-sm font-bold text-gray-900">{match.namespaceName}</span>
        {match.environment && <EnvironmentBadge env={match.environment} />}
      </div>
      <div className="text-[11px] font-semibold text-gray-500 uppercase tracking-wide">
        {getProviderServiceShortName(match.cloudProvider)}
      </div>
      <div className="text-xs font-mono text-gray-800 space-y-0.5">
        {match.entityKind === 'subscription' ? (
          <>
            <div>Topic: {match.topicName}</div>
            <div>Subscription: {match.subscriptionName}</div>
          </>
        ) : match.entityKind === 'topic' ? (
          <div>Topic: {match.entityName}</div>
        ) : (
          <div>{match.entityName}</div>
        )}
      </div>
      {match.dlqLabel && <div className="text-xs font-mono text-red-700">DLQ: {match.dlqLabel}</div>}
    </div>
  );
}

// Shows what a rule's EntityName/TopicName condition (if any) actually resolves to — the
// semantic content shared by the rule card's top-of-card scope strip (ScopeHeader, below)
// and the Replay All confirmation, where target ambiguity matters most. See lib/ruleScope.ts
// for why this is inferred rather than stored, and never fabricated when unresolvable.
function ScopeContent({ scope }: { scope: RuleScope }) {
  if (scope.kind === 'loading') {
    return <span className="text-xs text-gray-400 italic">Resolving scope…</span>;
  }

  if (scope.kind === 'global') {
    return (
      <div className="flex items-center gap-1.5 text-xs font-bold text-gray-600 tracking-wide">
        <Globe2 className="w-3.5 h-3.5 shrink-0" />
        ALL CLOUDS · ALL NAMESPACES
      </div>
    );
  }

  if (scope.kind === 'pattern') {
    return (
      <div>
        <p className="text-[10px] font-bold text-gray-500 uppercase tracking-wide mb-0.5">
          Pattern match — not a fixed namespace
        </p>
        <p className="text-xs font-mono text-gray-700">
          {fieldLabel(scope.field)} {operatorLabel(scope.operator)} &quot;{scope.value}&quot;
        </p>
      </div>
    );
  }

  if (scope.kind === 'unresolved') {
    return (
      <div>
        <p className="flex items-center gap-1 text-[10px] font-bold text-amber-700 uppercase tracking-wide mb-0.5">
          <AlertTriangle className="w-3 h-3" />
          Scope unresolved
        </p>
        <p className="text-xs text-amber-800">
          Target &quot;{scope.value}&quot; not found in any connected namespace
        </p>
      </div>
    );
  }

  const ambiguous = scope.matches.length > 1;
  return (
    <div className="space-y-2">
      {ambiguous && (
        <p className="flex items-center gap-1 text-[10px] font-bold text-amber-700 uppercase tracking-wide">
          <AlertTriangle className="w-3 h-3" />
          Ambiguous target — matches {scope.matches.length} namespaces
        </p>
      )}
      <div className="space-y-2">
        {scope.matches.map((m, i) => (
          <ScopeIdentity key={`${m.namespaceId}-${m.entityName}-${i}`} match={m} />
        ))}
      </div>
    </div>
  );
}

// Background/border tint for the rule card's top strip: the resolving provider's own brand
// tint for a precise single-namespace match (headerBg/headerBorder — the same tokens
// CloudBridgePage uses for its provider summary bar), amber for anything that needs an
// operator's attention (ambiguous or unresolved), neutral gray otherwise (global/pattern —
// true facts, not warnings).
function scopeStripClasses(scope: RuleScope): string {
  if (scope.kind === 'resolved' && scope.matches.length === 1) {
    const style = getProviderStyle(scope.matches[0].cloudProvider);
    return `${style.headerBg} ${style.headerBorder}`;
  }
  if (scope.kind === 'resolved' || scope.kind === 'unresolved') {
    return 'bg-amber-50 border-amber-200';
  }
  return 'bg-gray-50 border-gray-200';
}

// Top-of-card scope strip — the first thing an operator sees, so a rule card is
// self-contained without cross-referencing the Namespaces panel.
function ScopeHeader({ scope }: { scope: RuleScope }) {
  return (
    <div className={`px-4 py-2.5 border-b ${scopeStripClasses(scope)}`}>
      <ScopeContent scope={scope} />
    </div>
  );
}

function RuleCard({
  rule,
  scope,
  entityWarnings,
  onEdit,
  onDelete,
  onToggle,
  onTest,
  onReplayAll,
  isReplayingAll,
}: {
  rule: RuleResponse;
  scope: RuleScope;
  entityWarnings: string[];
  onEdit: () => void;
  onDelete: () => void;
  onToggle: () => void;
  onTest: () => void;
  onReplayAll: () => void;
  isReplayingAll: boolean;
}) {
  const successPct =
    rule.matchCount > 0
      ? Math.round((rule.successCount / rule.matchCount) * 100)
      : 0;
  // Mirrors the backend circuit-breaker threshold (30% over recent replays) so
  // users see WHY a rule is about to be — or should be — disabled.
  const isFailing = rule.enabled && rule.matchCount >= 5 && successPct < 30;
  const hasEntityWarning = entityWarnings.length > 0;

  return (
    <div
      className={`border rounded-xl overflow-hidden transition-all ${
        rule.enabled
          ? hasEntityWarning || isFailing
            ? 'border-amber-300 bg-amber-50/40 hover:shadow-md'
            : 'border-gray-200 bg-white hover:shadow-md'
          : 'border-gray-100 bg-gray-50 opacity-75'
      }`}
    >
      {/* Scope — cloud/namespace/provider/entity, self-contained at a glance (see ScopeHeader) */}
      <ScopeHeader scope={scope} />

      <div className="p-4">
      {/* Title Row */}
      <div className="flex items-start justify-between mb-3">
        <div className="flex items-center gap-2 min-w-0">
          <span
            className={`w-2.5 h-2.5 rounded-full shrink-0 ${
              rule.enabled ? 'bg-green-400' : 'bg-gray-300'
            }`}
          />
          {rule.name.startsWith('Auto:') && (
            <span
              className="shrink-0 px-1.5 py-0.5 text-[10px] font-bold text-amber-700 bg-amber-100 border border-amber-200 rounded"
              title="Rule source: AI-generated (Generate Intelligent Rules). Executes deterministically once enabled — this is not an autonomy grant."
            >
              AI-generated
            </span>
          )}
          <h3 className="text-sm font-bold text-gray-900 truncate">{rule.name}</h3>
          {hasEntityWarning && (
            <span
              className="shrink-0 flex items-center gap-1 px-1.5 py-0.5 text-[10px] font-bold text-amber-700 bg-amber-100 border border-amber-200 rounded"
              title={entityWarnings.join('\n')}
            >
              <AlertTriangle className="w-3 h-3" />
              entity missing
            </span>
          )}
          {isFailing && (
            <span
              className="shrink-0 px-1.5 py-0.5 text-[10px] font-bold text-red-700 bg-red-100 border border-red-200 rounded"
              title={`Only ${successPct}% of replays succeed — fix the root cause or disable this rule`}
            >
              failing
            </span>
          )}
        </div>
        <button
          onClick={onToggle}
          className="shrink-0 p-1 hover:bg-gray-100 rounded transition-colors"
          title={rule.enabled ? 'Disable rule' : 'Enable rule'}
          aria-label={rule.enabled ? 'Disable rule' : 'Enable rule'}
        >
          {rule.enabled ? (
            <ToggleRight className="w-5 h-5 text-green-500" />
          ) : (
            <ToggleLeft className="w-5 h-5 text-gray-400" />
          )}
        </button>
      </div>

      {rule.description && (
        <p className="text-xs text-gray-500 mb-3 line-clamp-2">{rule.description}</p>
      )}

      {/* Conditions */}
      <div className="mb-3">
        <h4 className="text-[10px] font-semibold text-gray-500 uppercase mb-1">Conditions</h4>
        <div className="space-y-0.5">
          {rule.conditions.slice(0, 3).map((c, i) => (
            <div key={i} className="flex items-center gap-1 text-xs text-gray-600">
              <span className="text-gray-400">•</span>
              <span className="lowercase">
                {fieldLabel(c.field)} {operatorLabel(c.operator)}{' '}
                <span className="font-mono text-gray-800">&quot;{c.value}&quot;</span>
              </span>
            </div>
          ))}
          {rule.conditions.length > 3 && (
            <span className="text-[10px] text-gray-400">
              +{rule.conditions.length - 3} more
            </span>
          )}
        </div>
      </div>

      {/* Action */}
      <div className="mb-3">
        <h4 className="text-[10px] font-semibold text-gray-500 uppercase mb-1">Action</h4>
        <div className="text-xs text-gray-600">
          {rule.action.autoReplay ? (
            <>
              <span className="text-green-600">&#10003;</span> Auto-replay after{' '}
              {rule.action.delaySeconds}s
              {rule.action.exponentialBackoff && (
                <span className="text-gray-400 ml-1">(backoff)</span>
              )}
            </>
          ) : (
            <span className="text-gray-400">No automatic action</span>
          )}
        </div>
      </div>

      {/* Stats */}
      <div className="mb-4 flex items-center gap-4 text-xs text-gray-500">
        <span>
          Pending:{' '}
          <strong className={rule.pendingMatchCount > 0 ? 'text-amber-600' : 'text-gray-700'}>
            {rule.pendingMatchCount}
          </strong>
        </span>
        <span>
          Replayed:{' '}
          <strong className="text-gray-700">
            {rule.matchCount}
          </strong>
        </span>
        <span>
          Success:{' '}
          <strong className="text-gray-700">
            {rule.successCount} ({successPct}%)
          </strong>
        </span>
        <span title="Rate limit — replays this rule will trigger per hour">
          Limit:{' '}
          <strong className="text-gray-700">{rule.maxReplaysPerHour}/hr</strong>
        </span>
      </div>

      {hasEntityWarning && (
        <div className="mb-3 px-2.5 py-2 bg-amber-50 border border-amber-200 rounded-lg text-[11px] text-amber-800 space-y-0.5">
          {entityWarnings.map((warning, i) => (
            <p key={i}>⚠ {warning}</p>
          ))}
        </div>
      )}

      {/* Actions */}
      <div className="flex items-center gap-1.5 border-t border-gray-100 pt-3">
        <button
          onClick={onTest}
          className="flex items-center gap-1 px-2.5 py-1.5 text-xs text-gray-600 border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-1"
        >
          <FlaskConical className="w-3.5 h-3.5" />
          Test
        </button>
        <button
          onClick={onReplayAll}
          disabled={!rule.enabled || isReplayingAll}
          className="flex items-center gap-1 px-2.5 py-1.5 text-xs text-amber-700 bg-amber-50 border border-amber-200 rounded-lg hover:bg-amber-100 transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
          title={
            !rule.enabled
              ? 'Enable rule first'
              : rule.pendingMatchCount === 0
                ? 'No pending DLQ messages match this rule'
                : `Replay ${rule.pendingMatchCount} matching DLQ messages`
          }
        >
          <Play className={`w-3.5 h-3.5 ${isReplayingAll ? 'animate-pulse' : ''}`} />
          {isReplayingAll ? 'Replaying...' : 'Replay All'}
        </button>
        <button
          onClick={onEdit}
          className="flex items-center gap-1 px-2.5 py-1.5 text-xs text-gray-600 border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-1"
        >
          <Pencil className="w-3.5 h-3.5" />
          Edit
        </button>
        <button
          onClick={onDelete}
          className="flex items-center gap-1 px-2.5 py-1.5 text-xs text-red-500 border border-gray-200 rounded-lg hover:bg-red-50 hover:border-red-200 transition-colors ml-auto focus:outline-none focus:ring-2 focus:ring-red-400 focus:ring-offset-1"
          title="Delete rule"
          aria-label="Delete rule"
        >
          <Trash2 className="w-3.5 h-3.5" />
        </button>
      </div>
      </div>
    </div>
  );
}

function EmptyState({
  onCreate,
  onBrowseTemplates,
  onGenerateRules,
  isGenerating,
}: {
  onCreate: () => void;
  onBrowseTemplates: () => void;
  onGenerateRules: () => void;
  isGenerating: boolean;
}) {
  return (
    <div className="py-16 text-center">
      <Brain className="w-12 h-12 text-amber-300 mx-auto mb-4" />
      <h3 className="text-lg font-semibold text-gray-900 mb-1">No auto-replay rules yet</h3>
      <p className="text-sm text-gray-500 mb-6 max-w-md mx-auto">
        Let ServiceHub analyse your DLQ messages and automatically create intelligent replay rules,
        or create rules manually from scratch or templates.
      </p>
      <div className="flex items-center justify-center gap-3">
        <button
          onClick={onGenerateRules}
          disabled={isGenerating}
          className="flex items-center gap-1.5 px-4 py-2 bg-amber-700 hover:bg-amber-800 text-white rounded-lg text-sm font-medium transition-colors disabled:opacity-50"
        >
          <Brain className={`w-4 h-4 ${isGenerating ? 'animate-pulse' : ''}`} />
          {isGenerating ? 'Analysing DLQ Patterns...' : 'Generate Intelligent Rules'}
        </button>
        <button
          onClick={onBrowseTemplates}
          className="flex items-center gap-1.5 px-4 py-2 border border-gray-200 rounded-lg text-sm text-gray-700 hover:bg-gray-50 transition-colors focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-1"
        >
          <Zap className="w-4 h-4 text-amber-500" />
          Browse Templates
        </button>
        <button
          onClick={onCreate}
          className="flex items-center gap-1.5 px-4 py-2 border border-gray-200 rounded-lg text-sm text-gray-700 hover:bg-gray-50 transition-colors focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-1"
        >
          <Plus className="w-4 h-4" />
          Create Rule
        </button>
      </div>
    </div>
  );
}

// ─── Replay All Confirm Dialog ─────────────────────────────────────

function ReplayAllConfirmDialog({
  rule,
  scope,
  isExecuting,
  onConfirm,
  onCancel,
}: {
  rule: RuleResponse | null;
  scope: RuleScope | null;
  isExecuting: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  // Escape-to-close — disarmed while the replay is actually executing, mirroring the
  // backdrop-click and Cancel button's own disabled state.
  useEffect(() => {
    if (!rule) return;
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && !isExecuting) {
        onCancel();
      }
    };
    window.addEventListener('keydown', handleEscape);
    return () => window.removeEventListener('keydown', handleEscape);
  }, [rule, isExecuting, onCancel]);

  // Declared before the early return — hooks must run on every render.
  const dialogRef = useFocusTrap<HTMLDivElement>(!!rule);

  if (!rule) return null;

  return createPortal(
    <div className="fixed inset-0 z-50 flex items-center justify-center">
      {/* Backdrop */}
      <div className="absolute inset-0 bg-black/60" onClick={isExecuting ? undefined : onCancel} aria-hidden="true" />

      {/* Dialog */}
      <div
        className="relative bg-white rounded-xl shadow-2xl w-full max-w-lg overflow-hidden"
        ref={dialogRef}
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="replay-all-dialog-title"
        aria-describedby="replay-all-dialog-description"
      >
        {/* Header — Danger style */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-red-100 bg-red-50">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 bg-red-100 border border-red-200 rounded-full flex items-center justify-center">
              <AlertTriangle className="w-5 h-5 text-red-600" />
            </div>
            <div>
              <h2 id="replay-all-dialog-title" className="text-lg font-semibold text-gray-900">Replay All Matching Messages</h2>
              <p className="text-xs text-red-600 font-medium mt-0.5">Destructive Operation</p>
            </div>
          </div>
          {!isExecuting && (
            <button onClick={onCancel} className="p-1 hover:bg-red-100 rounded-lg transition-colors" aria-label="Close dialog">
              <X className="w-5 h-5 text-gray-500" />
            </button>
          )}
        </div>

        {/* Body */}
        <div id="replay-all-dialog-description" className="px-6 py-5 space-y-4">
          {/* Rule info */}
          <div className="bg-gray-50 border border-gray-200 rounded-lg px-4 py-3">
            <div className="text-sm font-semibold text-gray-900 mb-1">
              Rule: {rule.name}
            </div>
            {scope && (
              <div className="mb-2">
                <ScopeContent scope={scope} />
              </div>
            )}
            <div className="space-y-0.5">
              {rule.conditions.map((c, i) => (
                <div key={i} className="text-xs text-gray-600 flex items-center gap-1">
                  <span className="text-gray-400">•</span>
                  <span>
                    {fieldLabel(c.field)} {operatorLabel(c.operator)}{' '}
                    <span className="font-mono text-gray-800">&quot;{c.value}&quot;</span>
                  </span>
                </div>
              ))}
            </div>
            {rule.action.targetEntity && (
              <div className="mt-2 text-xs text-amber-700 bg-amber-50 px-2 py-1 rounded">
                Target: <strong>{rule.action.targetEntity}</strong> (not original entity)
              </div>
            )}
          </div>

          {/* Warning messages */}
          <div className="space-y-3">
            <div className="flex items-start gap-2.5">
              <Shield className="w-4 h-4 text-red-500 shrink-0 mt-0.5" />
              <div className="text-sm text-gray-700">
                <strong className="text-red-700">Messages will be moved from the DLQ back to the active queue.</strong>{' '}
                This cannot be undone. The <strong>active message count will increase</strong> — this is expected.
              </div>
            </div>
            <div className="flex items-start gap-2.5">
              <AlertTriangle className="w-4 h-4 text-amber-500 shrink-0 mt-0.5" />
              <div className="text-sm text-gray-700">
                If the root cause hasn&apos;t been fixed, replayed messages may{' '}
                <strong>end up back in the DLQ</strong>, creating a replay loop.
              </div>
            </div>
            <div className="flex items-start gap-2.5">
              <AlertTriangle className="w-4 h-4 text-amber-500 shrink-0 mt-0.5" />
              <div className="text-sm text-gray-700">
                Replaying to the <strong>wrong entity</strong> or at <strong>high volume</strong> may
                cause downstream service disruption.
              </div>
            </div>
          </div>

          {/* Safety tip */}
          <div className="bg-blue-50 border border-blue-200 rounded-lg px-4 py-3">
            <p className="text-xs text-blue-800">
              <strong>Tip:</strong> Use the <strong>Test</strong> button first to see how many
              messages match. Only proceed if you are confident the root cause is resolved.
            </p>
          </div>
        </div>

        {/* Footer */}
        <div className="flex items-center justify-end gap-3 px-6 py-4 border-t border-gray-200 bg-gray-50">
          <button
            onClick={onCancel}
            disabled={isExecuting}
            className="px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-100 rounded-lg transition-colors disabled:opacity-50"
            autoFocus
          >
            Cancel
          </button>
          <button
            onClick={onConfirm}
            disabled={isExecuting}
            className="flex items-center gap-2 px-4 py-2 text-sm font-medium text-white bg-red-600 hover:bg-red-700 rounded-lg transition-colors disabled:opacity-60"
          >
            {isExecuting ? (
              <>
                <RefreshCw className="w-4 h-4 animate-spin" />
                Replaying...
              </>
            ) : (
              <>
                <Play className="w-4 h-4" />
                Yes, Replay All Matches
              </>
            )}
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
}

// ─── Helpers ───────────────────────────────────────────────────────

const FIELD_LABELS: Record<string, string> = {
  DeadLetterReason: 'reason',
  DeadLetterErrorDescription: 'error',
  FailureCategory: 'category',
  EntityName: 'entity',
  DeliveryCount: 'delivery count',
  ContentType: 'content type',
  TopicName: 'topic',
  CorrelationId: 'correlation ID',
  BodyPreview: 'body',
  ApplicationProperty: 'app property',
};

const OPERATOR_LABELS: Record<string, string> = {
  Contains: 'contains',
  NotContains: 'doesn\'t contain',
  Equals: 'equals',
  NotEquals: 'doesn\'t equal',
  StartsWith: 'starts with',
  EndsWith: 'ends with',
  Regex: 'matches',
  GreaterThan: '>',
  LessThan: '<',
  In: 'in',
};

function fieldLabel(field: string): string {
  return FIELD_LABELS[field] ?? field;
}

function operatorLabel(op: string): string {
  return OPERATOR_LABELS[op] ?? op;
}
