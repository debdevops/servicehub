import { useState } from 'react';
import { ChevronDown, ChevronUp, Lock, CheckCircle2, AlertCircle, HelpCircle } from 'lucide-react';
import type { DlqClusterSignature } from '@servicehub/ui-shared/lib/api/dlqSignatures';
import type { Namespace } from '@servicehub/ui-shared/lib/api/types';
import type { DlqSummary } from '@servicehub/ui-shared/lib/api/dlqHistory';
import { useSignatureAutonomyStatus } from '@servicehub/ui-shared/hooks/useRecoveryLedger';
import { ProviderBadge } from '@servicehub/ui-shared/lib/providerStyles';
import { EnvironmentBadge } from '@/components/EnvironmentBadge';
import { StatusBadge, TrendBadge } from './StatusBadge';
import { getTrendRecommendation } from './signatureRecommendations';

/**
 * A signature's automatic-replay eligibility, rendered from the same real data the backend's
 * Eligibility Gate predicate 5 consults (`useSignatureAutonomyStatus` → `GET
 * /recovery/autonomy/{signatureHash}`) — never a guessed or fabricated status.
 */
export function AutonomyStatus({ signatureHash }: { signatureHash: string }) {
  const { data: status, isLoading } = useSignatureAutonomyStatus(signatureHash);

  if (isLoading || !status) {
    return <span className="text-xs text-gray-400">Checking auto-replay eligibility…</span>;
  }

  if (status.canAutoReplay) {
    return (
      <div className="flex items-start gap-1.5">
        <CheckCircle2 className="w-3.5 h-3.5 text-green-600 shrink-0 mt-0.5" />
        <span className="text-xs text-green-700 font-medium">
          Automatic recovery available — {status.levelLabel}
        </span>
      </div>
    );
  }

  if (status.canProveDlqAbsence) {
    // Provider is capable of earning L4/L5; this signature just hasn't (yet) — a trust/evidence
    // gap, not a hard capability block.
    return (
      <div className="flex items-start gap-1.5">
        <AlertCircle className="w-3.5 h-3.5 text-amber-500 shrink-0 mt-0.5" />
        <span className="text-xs text-amber-700">
          <span className="font-medium">Manual approval required</span> ({status.levelLabel}) — {status.blockedReason}
        </span>
      </div>
    );
  }

  return (
    <div className="flex items-start gap-1.5">
      <Lock className="w-3.5 h-3.5 text-gray-500 shrink-0 mt-0.5" />
      <span className="text-xs text-gray-600">
        <span className="font-medium text-gray-800">Automatic recovery blocked.</span> {status.blockedReason} Manual recovery remains available.
      </span>
    </div>
  );
}

function formatRelativeTime(iso: string): string {
  const then = new Date(iso).getTime();
  const now = Date.now();
  const diffMs = Math.max(0, now - then);
  const minutes = Math.floor(diffMs / 60_000);
  if (minutes < 1) return 'just now';
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  return `${days}d ago`;
}

interface SignatureSummaryCardProps {
  signature: DlqClusterSignature;
  namespace?: Namespace;
  /** Namespace's DLQ summary, if the caller already has it — used only to compute "% of DLQ"
   * from real data; omitted entirely (not guessed) when unavailable. */
  dlqSummary?: DlqSummary;
  /** Only available on the Signature Details page's fuller response — "High" or "Medium". */
  confidence?: string;
  defaultExpanded?: boolean;
}

/**
 * Operator-facing summary of one failure signature: what's failing, where, how many messages,
 * ServiceHub's confidence and likely cause in plain language, and whether automatic replay is
 * available — with technical detail behind progressive disclosure. Every field here is either a
 * real backend value or a real value joined client-side from data already loaded elsewhere
 * (namespace, DLQ summary) — nothing is invented. Fields the backend doesn't provide (severity,
 * an exact last-occurrence timestamp distinct from the clustering window) are omitted rather than
 * guessed.
 */
export function SignatureSummaryCard({ signature, namespace, dlqSummary, confidence, defaultExpanded }: SignatureSummaryCardProps) {
  const [expanded, setExpanded] = useState(!!defaultExpanded);

  const percentOfDlq =
    dlqSummary && dlqSummary.activeMessages > 0
      ? Math.round((signature.size / dlqSummary.activeMessages) * 100)
      : null;

  const knowledge = signature.knowledge;
  const rootCause = knowledge?.rootCause;
  const whatYouCanDo =
    knowledge?.resolutionNotes ||
    knowledge?.replayGuidance ||
    getTrendRecommendation(signature.trend) ||
    'Review the affected messages and consumer logs before replaying them.';

  const isLowerConfidence = confidence === 'Medium';

  return (
    <div className="bg-white border border-gray-200 rounded-xl p-4 space-y-3">
      {/* Identity */}
      <div className="flex items-center gap-2 flex-wrap text-xs">
        {namespace?.cloudProvider && <ProviderBadge provider={namespace.cloudProvider} />}
        {namespace?.environment && <EnvironmentBadge env={namespace.environment} />}
        <span className="text-gray-500">
          {namespace?.displayName || namespace?.name || 'Unknown namespace'}
        </span>
        <span className="text-gray-300">·</span>
        <span className="font-medium text-gray-700">{signature.dominantEntity}</span>
      </div>

      {/* Counts, status, trend */}
      <div className="flex items-center gap-2 flex-wrap">
        <StatusBadge status={signature.status} />
        <TrendBadge trend={signature.trend} />
        <span className="text-xs text-gray-500">
          {signature.size} message{signature.size === 1 ? '' : 's'}
          {percentOfDlq !== null && ` · ${percentOfDlq}% of this namespace's DLQ`}
        </span>
        <span className="text-xs text-gray-500">· first seen {formatRelativeTime(signature.firstSeenAt)}</span>
        <span className="text-xs text-gray-500">· active as of {formatRelativeTime(signature.windowEnd)}</span>
      </div>

      {/* Plain-language explanation + what to do */}
      <div className="space-y-1.5">
        {isLowerConfidence && (
          <p className="text-xs text-gray-500 flex items-start gap-1.5">
            <HelpCircle className="w-3.5 h-3.5 shrink-0 mt-0.5" />
            ServiceHub has medium confidence in this classification — review the technical evidence below before relying on it fully.
          </p>
        )}
        <p className="text-sm font-medium text-gray-900">{signature.dominantDeadletterReason}</p>
        <p className="text-sm text-gray-900">
          {rootCause ? (
            <>
              <span className="font-medium">Likely cause (recorded by an operator):</span> {rootCause}
            </>
          ) : (
            signature.explanation
          )}
        </p>
        <p className="text-sm text-gray-600">
          <span className="font-medium text-gray-700">What you can do: </span>
          {whatYouCanDo}
        </p>
      </div>

      {/* Autonomy / replay status */}
      <div className="pt-1 border-t border-gray-100">
        <AutonomyStatus signatureHash={signature.signatureHash} />
      </div>

      {/* Progressive disclosure: technical evidence */}
      <button
        onClick={() => setExpanded((v) => !v)}
        className="flex items-center gap-1 text-xs text-gray-500 hover:text-gray-700 font-medium"
      >
        {expanded ? <ChevronUp className="w-3.5 h-3.5" /> : <ChevronDown className="w-3.5 h-3.5" />}
        {expanded ? 'Hide technical details' : 'Show technical details'}
      </button>

      {expanded && (
        <dl className="text-xs text-gray-600 grid grid-cols-2 gap-x-4 gap-y-1.5 bg-gray-50 rounded-lg p-3">
          <dt className="text-gray-400">Dominant dead-letter reason</dt>
          <dd className="text-gray-700">{signature.dominantDeadletterReason} ({signature.dominantDeadletterReasonCount}/{signature.size})</dd>
          <dt className="text-gray-400">Occurrence count</dt>
          <dd className="text-gray-700">{signature.occurrenceCount}</dd>
          {confidence && (
            <>
              <dt className="text-gray-400">Classification confidence</dt>
              <dd className="text-gray-700">{confidence}</dd>
            </>
          )}
          {signature.topTerms.length > 0 && (
            <>
              <dt className="text-gray-400">Top terms</dt>
              <dd className="text-gray-700">{signature.topTerms.join(', ')}</dd>
            </>
          )}
          <dt className="text-gray-400">Signature hash</dt>
          <dd className="text-gray-700 font-mono truncate" title={signature.signatureHash}>{signature.signatureHash}</dd>
        </dl>
      )}
    </div>
  );
}
