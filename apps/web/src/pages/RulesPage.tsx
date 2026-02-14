import { useState } from 'react';
import { Plus, Zap, RefreshCw, ToggleLeft, ToggleRight, Pencil, Trash2, FlaskConical } from 'lucide-react';
import { RuleBuilderDialog, TemplateGalleryDialog, RuleTestDialog } from '@/components/rules';
import {
  useRules,
  useCreateRule,
  useUpdateRule,
  useDeleteRule,
  useToggleRule,
} from '@/hooks/useRules';
import type {
  RuleResponse,
  RuleCondition,
  RuleAction,
  RuleTemplateResponse,
  CreateRuleRequest,
} from '@/lib/api/rules';

export function RulesPage() {
  const { data: rules, isLoading, refetch, isFetching } = useRules();
  const createMutation = useCreateRule();
  const updateMutation = useUpdateRule();
  const deleteMutation = useDeleteRule();
  const toggleMutation = useToggleRule();

  // Dialog state
  const [showBuilder, setShowBuilder] = useState(false);
  const [showTemplates, setShowTemplates] = useState(false);
  const [showTest, setShowTest] = useState(false);
  const [editRule, setEditRule] = useState<RuleResponse | null>(null);
  const [testRule, setTestRule] = useState<RuleResponse | null>(null);
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
    if (confirm(`Delete rule "${rule.name}"? This cannot be undone.`)) {
      deleteMutation.mutate(rule.id);
    }
  };

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      {/* Header */}
      <div className="bg-white border-b border-gray-200 px-6 py-4 shrink-0">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-xl font-bold text-gray-900">Auto-Replay Rules</h1>
            <p className="text-sm text-gray-500 mt-0.5">
              Define rules that automatically replay dead-letter messages matching specific conditions
            </p>
          </div>
          <div className="flex items-center gap-2">
            <button
              onClick={() => setShowTemplates(true)}
              className="flex items-center gap-1.5 px-3 py-2 border border-gray-200 rounded-lg text-sm text-gray-700 hover:bg-gray-50 transition-colors"
            >
              <Zap className="w-4 h-4 text-amber-500" />
              Browse Templates
            </button>
            <button
              onClick={handleCreate}
              className="flex items-center gap-1.5 px-3 py-2 bg-primary-500 hover:bg-primary-600 text-white rounded-lg text-sm font-medium transition-colors"
            >
              <Plus className="w-4 h-4" />
              Create Rule
            </button>
            <button
              onClick={() => refetch()}
              disabled={isFetching}
              className="p-2 border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors"
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
          <div className="py-12 text-center text-sm text-gray-500">Loading rules...</div>
        ) : rules && rules.length > 0 ? (
          <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
            {rules.map((rule) => (
              <RuleCard
                key={rule.id}
                rule={rule}
                onEdit={() => handleEdit(rule)}
                onDelete={() => handleDelete(rule)}
                onToggle={() => toggleMutation.mutate(rule.id)}
                onTest={() => handleTest(rule)}
              />
            ))}
          </div>
        ) : (
          <EmptyState
            onCreate={handleCreate}
            onBrowseTemplates={() => setShowTemplates(true)}
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
    </div>
  );
}

// ─── Sub-Components ────────────────────────────────────────────────

function RuleCard({
  rule,
  onEdit,
  onDelete,
  onToggle,
  onTest,
}: {
  rule: RuleResponse;
  onEdit: () => void;
  onDelete: () => void;
  onToggle: () => void;
  onTest: () => void;
}) {
  const successPct =
    rule.matchCount > 0
      ? Math.round((rule.successCount / rule.matchCount) * 100)
      : 0;

  return (
    <div
      className={`border rounded-xl p-4 transition-all ${
        rule.enabled
          ? 'border-gray-200 bg-white hover:shadow-md'
          : 'border-gray-100 bg-gray-50 opacity-75'
      }`}
    >
      {/* Title Row */}
      <div className="flex items-start justify-between mb-3">
        <div className="flex items-center gap-2 min-w-0">
          <span
            className={`w-2.5 h-2.5 rounded-full shrink-0 ${
              rule.enabled ? 'bg-green-400' : 'bg-gray-300'
            }`}
          />
          <h3 className="text-sm font-bold text-gray-900 truncate">{rule.name}</h3>
        </div>
        <button
          onClick={onToggle}
          className="shrink-0 p-1 hover:bg-gray-100 rounded transition-colors"
          title={rule.enabled ? 'Disable rule' : 'Enable rule'}
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
          Matched: <strong className="text-gray-700">{rule.matchCount}</strong>
        </span>
        <span>
          Success:{' '}
          <strong className="text-gray-700">
            {rule.successCount} ({successPct}%)
          </strong>
        </span>
      </div>

      {/* Actions */}
      <div className="flex items-center gap-1.5 border-t border-gray-100 pt-3">
        <button
          onClick={onTest}
          className="flex items-center gap-1 px-2.5 py-1.5 text-xs text-gray-600 border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors"
        >
          <FlaskConical className="w-3.5 h-3.5" />
          Test
        </button>
        <button
          onClick={onEdit}
          className="flex items-center gap-1 px-2.5 py-1.5 text-xs text-gray-600 border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors"
        >
          <Pencil className="w-3.5 h-3.5" />
          Edit
        </button>
        <button
          onClick={onDelete}
          className="flex items-center gap-1 px-2.5 py-1.5 text-xs text-red-500 border border-gray-200 rounded-lg hover:bg-red-50 hover:border-red-200 transition-colors ml-auto"
        >
          <Trash2 className="w-3.5 h-3.5" />
        </button>
      </div>
    </div>
  );
}

function EmptyState({
  onCreate,
  onBrowseTemplates,
}: {
  onCreate: () => void;
  onBrowseTemplates: () => void;
}) {
  return (
    <div className="py-16 text-center">
      <Zap className="w-12 h-12 text-primary-300 mx-auto mb-4" />
      <h3 className="text-lg font-semibold text-gray-900 mb-1">No auto-replay rules yet</h3>
      <p className="text-sm text-gray-500 mb-6 max-w-md mx-auto">
        Create rules that automatically replay dead-letter messages when they match specific
        conditions. Start from scratch or use a template.
      </p>
      <div className="flex items-center justify-center gap-3">
        <button
          onClick={onBrowseTemplates}
          className="flex items-center gap-1.5 px-4 py-2 border border-gray-200 rounded-lg text-sm text-gray-700 hover:bg-gray-50 transition-colors"
        >
          <Zap className="w-4 h-4 text-amber-500" />
          Browse Templates
        </button>
        <button
          onClick={onCreate}
          className="flex items-center gap-1.5 px-4 py-2 bg-primary-500 hover:bg-primary-600 text-white rounded-lg text-sm font-medium transition-colors"
        >
          <Plus className="w-4 h-4" />
          Create Rule
        </button>
      </div>
    </div>
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
