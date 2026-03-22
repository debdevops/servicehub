import { useState } from 'react';
import { Shield, AlertTriangle, CheckCircle, HelpCircle, Loader2, Search } from 'lucide-react';
import { dlqHistoryApi } from '@/lib/api/dlqHistory';
import type { ForensicResult } from '@/lib/api/dlqHistory';
import type { Message } from '@/lib/mockData';

interface ForensicTabProps {
  message: Message;
  onForensicResult?: (replaySafety: string | null) => void;
}

const SAFETY_CONFIG: Record<string, { icon: typeof CheckCircle; color: string; bg: string; label: string }> = {
  Safe: { icon: CheckCircle, color: 'text-green-600', bg: 'bg-green-50 border-green-200', label: 'Safe to Replay' },
  Unsafe: { icon: AlertTriangle, color: 'text-red-600', bg: 'bg-red-50 border-red-200', label: 'Unsafe — Do Not Replay' },
  RequiresReview: { icon: HelpCircle, color: 'text-amber-600', bg: 'bg-amber-50 border-amber-200', label: 'Requires Manual Review' },
};

function ConfidenceBar({ value }: { value: number }) {
  const perc = Math.round(value * 100);
  const color = perc >= 90 ? 'bg-green-500' : perc >= 70 ? 'bg-amber-500' : 'bg-red-400';

  return (
    <div className="flex items-center gap-3">
      <div className="flex-1 h-2 bg-gray-200 rounded-full overflow-hidden">
        <div className={`h-full rounded-full ${color}`} style={{ width: `${perc}%` }} />
      </div>
      <span className="text-sm font-semibold text-gray-700 w-12 text-right">{perc}%</span>
    </div>
  );
}

export function ForensicTab({ message, onForensicResult }: ForensicTabProps) {
  const [result, setResult] = useState<ForensicResult | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Check if we have a DLQ history ID (numeric) from the message
  const dlqId = message.dlqId ?? null;

  const handleAnalyse = async () => {
    if (!dlqId) return;
    setIsLoading(true);
    setError(null);
    try {
      const res = await dlqHistoryApi.getForensicResult(dlqId);
      setResult(res);
      onForensicResult?.(res.replaySafety ?? null);
    } catch {
      setError('Failed to run forensic analysis. The API may be unavailable.');
    } finally {
      setIsLoading(false);
    }
  };

  // No DLQ ID available — this is a regular message, not from DLQ Intelligence
  if (!dlqId) {
    return (
      <div className="p-6">
        <div className="flex flex-col items-center justify-center py-16 text-gray-500">
          <div className="w-16 h-16 bg-gray-100 rounded-full flex items-center justify-center mb-4">
            <Shield size={32} className="text-gray-300" />
          </div>
          <p className="text-lg font-medium text-gray-700">Forensic Analysis</p>
          <p className="text-sm text-gray-400 mt-1 text-center max-w-sm">
            Forensic analysis is available for messages tracked by DLQ Intelligence.
            View a message from the DLQ History page to access this feature.
          </p>
        </div>
      </div>
    );
  }

  // Initial state — not yet analysed
  if (!result && !isLoading && !error) {
    return (
      <div className="p-6">
        <div className="flex flex-col items-center justify-center py-16">
          <div className="w-16 h-16 bg-primary-50 rounded-full flex items-center justify-center mb-4">
            <Search size={32} className="text-primary-400" />
          </div>
          <p className="text-lg font-medium text-gray-700">Run Forensic Analysis</p>
          <p className="text-sm text-gray-400 mt-1 text-center max-w-sm mb-6">
            Analyse this DLQ message through the three-tier forensic engine
            to determine root cause and replay safety.
          </p>
          <button
            onClick={handleAnalyse}
            className="px-5 py-2.5 bg-primary-500 hover:bg-primary-600 text-white rounded-lg text-sm font-medium transition-colors"
          >
            Analyse Message
          </button>
        </div>
      </div>
    );
  }

  // Loading state
  if (isLoading) {
    return (
      <div className="p-6">
        <div className="flex flex-col items-center justify-center py-16 text-gray-500">
          <Loader2 size={32} className="animate-spin text-primary-500 mb-4" />
          <p className="text-sm text-gray-400">Running forensic analysis...</p>
        </div>
      </div>
    );
  }

  // Error state
  if (error) {
    return (
      <div className="p-6">
        <div className="flex flex-col items-center justify-center py-16 text-gray-500">
          <AlertTriangle size={32} className="text-red-400 mb-4" />
          <p className="text-sm text-red-600">{error}</p>
          <button
            onClick={handleAnalyse}
            className="mt-4 px-4 py-2 bg-gray-100 hover:bg-gray-200 rounded-lg text-sm text-gray-700 transition-colors"
          >
            Retry
          </button>
        </div>
      </div>
    );
  }

  // Result display
  if (!result) return null;

  const safety = SAFETY_CONFIG[result.replaySafety] ?? SAFETY_CONFIG['RequiresReview'];
  const SafetyIcon = safety.icon;

  return (
    <div className="p-6 space-y-6">
      {/* Replay Safety Banner */}
      <div className={`flex items-center gap-3 p-4 rounded-xl border ${safety.bg}`}>
        <SafetyIcon size={24} className={safety.color} />
        <div>
          <p className={`font-semibold ${safety.color}`}>{safety.label}</p>
          <p className="text-xs text-gray-600 mt-0.5">
            Verdict: {result.replaySafety}
          </p>
        </div>
      </div>

      {/* Root Cause */}
      <div className="bg-white border border-gray-200 rounded-xl p-5">
        <h4 className="text-sm font-semibold text-gray-700 mb-2">Root Cause</h4>
        <p className="text-sm text-gray-600 leading-relaxed">{result.rootCause}</p>
      </div>

      {/* Metrics */}
      <div className="grid grid-cols-2 gap-4">
        <div className="bg-white border border-gray-200 rounded-xl p-4">
          <p className="text-xs font-semibold text-gray-500 uppercase mb-1">Category</p>
          <p className="text-lg font-bold text-gray-900">{result.failureCategory}</p>
        </div>
        <div className="bg-white border border-gray-200 rounded-xl p-4">
          <p className="text-xs font-semibold text-gray-500 uppercase mb-1">Analysis Tier</p>
          <p className="text-lg font-bold text-gray-900">{result.tier}</p>
        </div>
      </div>

      {/* Confidence */}
      <div className="bg-white border border-gray-200 rounded-xl p-5">
        <h4 className="text-sm font-semibold text-gray-700 mb-3">Confidence</h4>
        <ConfidenceBar value={result.confidence} />
      </div>

      {/* Re-analyse button */}
      <div className="flex justify-end">
        <button
          onClick={handleAnalyse}
          disabled={isLoading}
          className="px-4 py-2 bg-gray-100 hover:bg-gray-200 rounded-lg text-sm text-gray-700 font-medium transition-colors disabled:opacity-50"
        >
          Re-analyse
        </button>
      </div>
    </div>
  );
}
