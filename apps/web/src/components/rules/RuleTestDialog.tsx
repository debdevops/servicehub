import { X, CheckCircle, AlertTriangle, Loader2 } from 'lucide-react';
import { useTestRule } from '@/hooks/useRules';
import type { RuleCondition, RuleResponse, RuleTestResponse } from '@/lib/api/rules';
import { useState } from 'react';

interface RuleTestDialogProps {
  open: boolean;
  onClose: () => void;
  /** Either provide an existing rule ID or ad-hoc conditions. */
  rule?: RuleResponse | null;
  conditions?: RuleCondition[];
  namespaceId?: string;
}

export function RuleTestDialog({
  open,
  onClose,
  rule,
  conditions,
  namespaceId,
}: RuleTestDialogProps) {
  const testMutation = useTestRule();
  const [result, setResult] = useState<RuleTestResponse | null>(null);

  const handleTest = () => {
    setResult(null);
    testMutation.mutate(
      {
        ruleId: rule?.id,
        conditions: rule ? undefined : conditions,
        namespaceId,
        maxMessages: 100,
      },
      { onSuccess: setResult },
    );
  };

  // Auto-test on open
  const [hasTriggered, setHasTriggered] = useState(false);
  if (open && !hasTriggered) {
    setHasTriggered(true);
    handleTest();
  }
  if (!open && hasTriggered) {
    setHasTriggered(false);
    setResult(null);
  }

  if (!open) return null;

  const title = rule ? `Test Rule: ${rule.name}` : 'Test Rule Conditions';

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
      <div className="bg-white rounded-2xl shadow-2xl w-full max-w-lg mx-4">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200">
          <h2 className="text-lg font-bold text-gray-900">{title}</h2>
          <button
            onClick={onClose}
            className="p-1.5 hover:bg-gray-100 rounded-lg transition-colors"
          >
            <X className="w-5 h-5 text-gray-500" />
          </button>
        </div>

        {/* Body */}
        <div className="px-6 py-5">
          {testMutation.isPending && (
            <div className="flex items-center justify-center gap-2 py-8 text-gray-500">
              <Loader2 className="w-5 h-5 animate-spin" />
              <span className="text-sm">Testing against active DLQ messages...</span>
            </div>
          )}

          {testMutation.isError && !result && (
            <div className="flex items-center gap-2 py-6 text-red-600">
              <AlertTriangle className="w-5 h-5" />
              <span className="text-sm">Failed to run test. Please try again.</span>
            </div>
          )}

          {result && (
            <div className="space-y-4">
              {/* Summary */}
              <div className="flex items-center gap-2">
                <CheckCircle className="w-5 h-5 text-green-500" />
                <span className="text-sm font-semibold text-gray-900">Test Results</span>
              </div>

              <div className="grid grid-cols-3 gap-3">
                <StatCard label="Tested" value={result.totalTested} />
                <StatCard label="Matched" value={result.matchedCount} highlight />
                <StatCard
                  label="Est. Success"
                  value={`${result.estimatedSuccessRate}%`}
                />
              </div>

              <p className="text-sm text-gray-600">
                Would match{' '}
                <strong className="text-gray-900">{result.matchedCount}</strong> of{' '}
                <strong className="text-gray-900">{result.totalTested}</strong> messages
              </p>

              {/* Sample Matches */}
              {result.sampleMatches.length > 0 && (
                <div>
                  <h3 className="text-xs font-semibold text-gray-600 uppercase mb-2">
                    Sample Matched Messages
                  </h3>
                  <div className="space-y-1.5 max-h-40 overflow-y-auto">
                    {result.sampleMatches.map((m) => (
                      <div
                        key={m.messageId}
                        className="flex items-start gap-2 text-xs p-2 bg-green-50 rounded-lg"
                      >
                        <span className="shrink-0 text-green-500 mt-0.5">•</span>
                        <div className="min-w-0">
                          <span className="font-mono text-gray-700">
                            {m.serviceBusMessageId.slice(0, 12)}...
                          </span>
                          {m.deadLetterReason && (
                            <span className="text-gray-500 ml-1.5">
                              — {m.deadLetterReason}
                            </span>
                          )}
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              )}

              {result.sampleMatches.length === 0 && result.matchedCount === 0 && (
                <div className="py-3 text-center text-sm text-gray-500">
                  No messages matched the conditions. Try adjusting the rule.
                </div>
              )}
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="flex items-center justify-end gap-2 px-6 py-3 border-t border-gray-200">
          <button
            onClick={handleTest}
            disabled={testMutation.isPending}
            className="px-3 py-1.5 text-sm text-primary-700 border border-primary-200 rounded-lg hover:bg-primary-50 transition-colors disabled:opacity-50"
          >
            Re-test
          </button>
          <button
            onClick={onClose}
            className="px-4 py-1.5 text-sm font-medium text-white bg-primary-500 rounded-lg hover:bg-primary-600 transition-colors"
          >
            Close
          </button>
        </div>
      </div>
    </div>
  );
}

function StatCard({
  label,
  value,
  highlight,
}: {
  label: string;
  value: number | string;
  highlight?: boolean;
}) {
  return (
    <div
      className={`rounded-xl p-3 text-center ${
        highlight ? 'bg-primary-50' : 'bg-gray-50'
      }`}
    >
      <div
        className={`text-xl font-bold ${
          highlight ? 'text-primary-700' : 'text-gray-900'
        }`}
      >
        {typeof value === 'number' ? value.toLocaleString() : value}
      </div>
      <div className="text-xs text-gray-500">{label}</div>
    </div>
  );
}
