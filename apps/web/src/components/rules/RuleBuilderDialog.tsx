import { useState, useEffect } from 'react';
import { Plus, Trash2, Info } from 'lucide-react';
import type { RuleCondition, RuleAction, RuleResponse, CreateRuleRequest } from '@/lib/api/rules';

const FIELD_OPTIONS = [
  { value: 'DeadLetterReason', label: 'Dead Letter Reason' },
  { value: 'DeadLetterErrorDescription', label: 'Error Description' },
  { value: 'FailureCategory', label: 'Failure Category' },
  { value: 'EntityName', label: 'Entity Name' },
  { value: 'DeliveryCount', label: 'Delivery Count' },
  { value: 'ContentType', label: 'Content Type' },
  { value: 'TopicName', label: 'Topic Name' },
  { value: 'CorrelationId', label: 'Correlation ID' },
  { value: 'BodyPreview', label: 'Body Preview' },
  { value: 'ApplicationProperty', label: 'Application Property' },
];

const OPERATOR_OPTIONS = [
  { value: 'Contains', label: 'Contains' },
  { value: 'NotContains', label: 'Does not contain' },
  { value: 'Equals', label: 'Equals' },
  { value: 'NotEquals', label: 'Does not equal' },
  { value: 'StartsWith', label: 'Starts with' },
  { value: 'EndsWith', label: 'Ends with' },
  { value: 'Regex', label: 'Matches regex' },
  { value: 'GreaterThan', label: 'Greater than' },
  { value: 'LessThan', label: 'Less than' },
  { value: 'In', label: 'In (comma-separated)' },
];

interface RuleBuilderDialogProps {
  open: boolean;
  onClose: () => void;
  onSave: (request: CreateRuleRequest) => void;
  editRule?: RuleResponse | null;
  initialConditions?: RuleCondition[];
  initialAction?: RuleAction;
  isSaving?: boolean;
}

const defaultCondition: RuleCondition = {
  field: 'DeadLetterReason',
  operator: 'Contains',
  value: '',
};

const defaultAction: RuleAction = {
  autoReplay: true,
  delaySeconds: 60,
  maxRetries: 3,
  exponentialBackoff: false,
};

export function RuleBuilderDialog({
  open,
  onClose,
  onSave,
  editRule,
  initialConditions,
  initialAction,
  isSaving,
}: RuleBuilderDialogProps) {
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [enabled, setEnabled] = useState(true);
  const [conditions, setConditions] = useState<RuleCondition[]>([{ ...defaultCondition }]);
  const [action, setAction] = useState<RuleAction>({ ...defaultAction });
  const [maxReplaysPerHour, setMaxReplaysPerHour] = useState(100);

  // Populate form when editing or using template
  useEffect(() => {
    if (editRule) {
      setName(editRule.name);
      setDescription(editRule.description ?? '');
      setEnabled(editRule.enabled);
      setConditions(editRule.conditions.length > 0 ? editRule.conditions : [{ ...defaultCondition }]);
      setAction(editRule.action);
      setMaxReplaysPerHour(editRule.maxReplaysPerHour);
    } else if (initialConditions || initialAction) {
      setName('');
      setDescription('');
      setEnabled(true);
      setConditions(initialConditions?.length ? initialConditions : [{ ...defaultCondition }]);
      setAction(initialAction ?? { ...defaultAction });
      setMaxReplaysPerHour(100);
    } else {
      resetForm();
    }
  }, [editRule, initialConditions, initialAction, open]);

  const resetForm = () => {
    setName('');
    setDescription('');
    setEnabled(true);
    setConditions([{ ...defaultCondition }]);
    setAction({ ...defaultAction });
    setMaxReplaysPerHour(100);
  };

  const addCondition = () => {
    setConditions((prev) => [...prev, { ...defaultCondition }]);
  };

  const removeCondition = (index: number) => {
    setConditions((prev) => prev.filter((_, i) => i !== index));
  };

  const updateCondition = (index: number, updates: Partial<RuleCondition>) => {
    setConditions((prev) =>
      prev.map((c, i) => (i === index ? { ...c, ...updates } : c)),
    );
  };

  const handleSave = () => {
    if (!name.trim()) return;
    if (conditions.some((c) => !c.value.trim())) return;

    const request: CreateRuleRequest = {
      name: name.trim(),
      description: description.trim() || undefined,
      enabled,
      conditions,
      action,
      maxReplaysPerHour,
    };

    onSave(request);
  };

  const canSave = name.trim().length > 0 && conditions.every((c) => c.value.trim().length > 0);

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
      <div className="bg-white rounded-2xl shadow-2xl w-full max-w-2xl max-h-[90vh] flex flex-col mx-4">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200">
          <h2 className="text-lg font-bold text-gray-900">
            {editRule ? 'Edit Auto-Replay Rule' : 'Create Auto-Replay Rule'}
          </h2>
          <div className="flex items-center gap-2">
            <button
              onClick={onClose}
              className="px-3 py-1.5 text-sm text-gray-600 border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors"
            >
              Cancel
            </button>
            <button
              onClick={handleSave}
              disabled={!canSave || isSaving}
              className="px-4 py-1.5 text-sm font-medium text-white bg-primary-500 rounded-lg hover:bg-primary-600 disabled:opacity-50 transition-colors"
            >
              {isSaving ? 'Saving...' : 'Save'}
            </button>
          </div>
        </div>

        {/* Body */}
        <div className="flex-1 overflow-y-auto px-6 py-5 space-y-6">
          {/* Basic Info */}
          <div className="space-y-3">
            <div>
              <label className="block text-xs font-semibold text-gray-600 uppercase mb-1">
                Rule Name
              </label>
              <input
                type="text"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="e.g., Database Timeouts"
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-primary-500 focus:border-primary-500"
              />
            </div>
            <div>
              <label className="block text-xs font-semibold text-gray-600 uppercase mb-1">
                Description
              </label>
              <textarea
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                placeholder="Describe what this rule does..."
                rows={2}
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-primary-500 focus:border-primary-500 resize-none"
              />
            </div>
            <label className="flex items-center gap-2 cursor-pointer">
              <input
                type="checkbox"
                checked={enabled}
                onChange={(e) => setEnabled(e.target.checked)}
                className="w-4 h-4 rounded border-gray-300 text-primary-500 focus:ring-primary-500"
              />
              <span className="text-sm text-gray-700">Enable this rule</span>
            </label>
          </div>

          {/* Conditions */}
          <div>
            <div className="flex items-center justify-between mb-2">
              <label className="text-xs font-semibold text-gray-600 uppercase">
                Conditions (all must match)
              </label>
              <button
                onClick={addCondition}
                className="flex items-center gap-1 text-xs text-primary-600 hover:text-primary-700 font-medium"
              >
                <Plus className="w-3.5 h-3.5" />
                Add Condition
              </button>
            </div>

            <div className="space-y-3">
              {conditions.map((condition, i) => (
                <div
                  key={i}
                  className="border border-gray-200 rounded-xl p-3 bg-gray-50 space-y-2"
                >
                  <div className="flex items-center gap-2">
                    <div className="flex-1 grid grid-cols-3 gap-2">
                      <div>
                        <label className="block text-[10px] text-gray-500 uppercase mb-0.5">
                          Field
                        </label>
                        <select
                          value={condition.field}
                          onChange={(e) => updateCondition(i, { field: e.target.value })}
                          className="w-full px-2 py-1.5 border border-gray-300 rounded-lg text-sm bg-white"
                        >
                          {FIELD_OPTIONS.map((f) => (
                            <option key={f.value} value={f.value}>
                              {f.label}
                            </option>
                          ))}
                        </select>
                      </div>
                      <div>
                        <label className="block text-[10px] text-gray-500 uppercase mb-0.5">
                          Operator
                        </label>
                        <select
                          value={condition.operator}
                          onChange={(e) => updateCondition(i, { operator: e.target.value })}
                          className="w-full px-2 py-1.5 border border-gray-300 rounded-lg text-sm bg-white"
                        >
                          {OPERATOR_OPTIONS.map((o) => (
                            <option key={o.value} value={o.value}>
                              {o.label}
                            </option>
                          ))}
                        </select>
                      </div>
                      <div>
                        <label className="block text-[10px] text-gray-500 uppercase mb-0.5">
                          Value
                        </label>
                        <input
                          type="text"
                          value={condition.value}
                          onChange={(e) => updateCondition(i, { value: e.target.value })}
                          placeholder="Value..."
                          className="w-full px-2 py-1.5 border border-gray-300 rounded-lg text-sm"
                        />
                      </div>
                    </div>
                    {conditions.length > 1 && (
                      <button
                        onClick={() => removeCondition(i)}
                        className="p-1.5 text-red-400 hover:text-red-600 hover:bg-red-50 rounded-lg transition-colors mt-4"
                        title="Remove condition"
                      >
                        <Trash2 className="w-4 h-4" />
                      </button>
                    )}
                  </div>

                  {condition.field === 'ApplicationProperty' && (
                    <div>
                      <label className="block text-[10px] text-gray-500 uppercase mb-0.5">
                        Property Key
                      </label>
                      <input
                        type="text"
                        value={condition.propertyKey ?? ''}
                        onChange={(e) => updateCondition(i, { propertyKey: e.target.value })}
                        placeholder="e.g., x-retry-count"
                        className="w-full px-2 py-1.5 border border-gray-300 rounded-lg text-sm"
                      />
                    </div>
                  )}
                </div>
              ))}
            </div>
          </div>

          {/* Actions */}
          <div>
            <label className="block text-xs font-semibold text-gray-600 uppercase mb-2">
              Actions
            </label>
            <div className="border border-gray-200 rounded-xl p-4 bg-gray-50 space-y-3">
              <label className="flex items-center gap-2 cursor-pointer">
                <input
                  type="checkbox"
                  checked={action.autoReplay}
                  onChange={(e) => setAction((a) => ({ ...a, autoReplay: e.target.checked }))}
                  className="w-4 h-4 rounded border-gray-300 text-primary-500 focus:ring-primary-500"
                />
                <span className="text-sm text-gray-700 font-medium">
                  Auto-replay messages that match
                </span>
              </label>

              {action.autoReplay && (
                <div className="grid grid-cols-2 gap-3 pl-6">
                  <div>
                    <label className="block text-[10px] text-gray-500 uppercase mb-0.5">
                      Delay (seconds)
                    </label>
                    <input
                      type="number"
                      min={0}
                      max={86400}
                      value={action.delaySeconds}
                      onChange={(e) =>
                        setAction((a) => ({ ...a, delaySeconds: parseInt(e.target.value) || 0 }))
                      }
                      className="w-full px-2 py-1.5 border border-gray-300 rounded-lg text-sm"
                    />
                  </div>
                  <div>
                    <label className="block text-[10px] text-gray-500 uppercase mb-0.5">
                      Max Retries
                    </label>
                    <input
                      type="number"
                      min={1}
                      max={10}
                      value={action.maxRetries}
                      onChange={(e) =>
                        setAction((a) => ({ ...a, maxRetries: parseInt(e.target.value) || 1 }))
                      }
                      className="w-full px-2 py-1.5 border border-gray-300 rounded-lg text-sm"
                    />
                  </div>
                  <label className="flex items-center gap-2 cursor-pointer col-span-2">
                    <input
                      type="checkbox"
                      checked={action.exponentialBackoff}
                      onChange={(e) =>
                        setAction((a) => ({ ...a, exponentialBackoff: e.target.checked }))
                      }
                      className="w-4 h-4 rounded border-gray-300 text-primary-500 focus:ring-primary-500"
                    />
                    <span className="text-sm text-gray-700">Exponential backoff</span>
                  </label>
                  <div className="col-span-2">
                    <label className="block text-[10px] text-gray-500 uppercase mb-0.5">
                      Target Entity (optional — leave blank for original)
                    </label>
                    <input
                      type="text"
                      value={action.targetEntity ?? ''}
                      onChange={(e) =>
                        setAction((a) => ({
                          ...a,
                          targetEntity: e.target.value || undefined,
                        }))
                      }
                      placeholder="e.g., fallback-queue"
                      className="w-full px-2 py-1.5 border border-gray-300 rounded-lg text-sm"
                    />
                  </div>
                </div>
              )}
            </div>
          </div>

          {/* Rate Limiting */}
          <div>
            <label className="block text-xs font-semibold text-gray-600 uppercase mb-1">
              Max Replays Per Hour
            </label>
            <input
              type="number"
              min={1}
              max={10000}
              value={maxReplaysPerHour}
              onChange={(e) => setMaxReplaysPerHour(parseInt(e.target.value) || 100)}
              className="w-32 px-3 py-2 border border-gray-300 rounded-lg text-sm"
            />
          </div>

          {/* Safety Notice */}
          <div className="flex items-start gap-2 p-3 bg-amber-50 border border-amber-200 rounded-xl text-xs text-amber-800">
            <Info className="w-4 h-4 shrink-0 mt-0.5" />
            <span>
              <strong>Safety:</strong> Circuit breaker will automatically disable this rule
              if the success rate drops below 30% over the last 50 replays.
            </span>
          </div>
        </div>
      </div>
    </div>
  );
}
