import { Link } from 'react-router-dom';
import {
  Gauge, ShieldAlert, RefreshCw, AlertCircle, Info, Zap, History, ClipboardList,
  Eye, Sparkles, CheckCircle2, Lock, ShieldCheck, ShieldQuestion, Cloud,
} from 'lucide-react';
import { useAutonomyDashboard, useApprovalQueue } from '@servicehub/ui-shared/hooks/useRecoveryLedger';
import { usePlaybookEntries } from '@servicehub/ui-shared/hooks/usePlaybookLedger';
import { useProviderCapabilities } from '@servicehub/ui-shared/hooks/useCloudBridge';
import { useMe } from '@servicehub/ui-shared/hooks/useMe';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';
import type {
  AutonomyGrantSummary,
  AutonomyTransitionSummary,
  CircuitBreakerTrip,
} from '@servicehub/ui-shared/lib/api/recovery';
import type { PlaybookEntry } from '@servicehub/ui-shared/lib/api/playbook';

// L0-L2 never appear here (no AutonomyGrant row exists until a signature is first promoted past
// the L3 floor — see AutonomyGrant's doc comment); L3 is the permanent human-approved floor a
// demotion can land back on, L4/L5 are earned unattended trust.
const LEVEL_STYLES: Record<number, string> = {
  3: 'bg-gray-100 text-gray-700 border-gray-300',
  4: 'bg-blue-50 text-blue-700 border-blue-300',
  5: 'bg-green-50 text-green-700 border-green-300',
};

function LevelBadge({ level, label }: { level: number; label: string }) {
  const style = LEVEL_STYLES[level] ?? 'bg-gray-100 text-gray-700 border-gray-300';
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium border ${style}`}>
      {label}
    </span>
  );
}

function truncateHash(hash: string): string {
  return hash.length > 16 ? `${hash.substring(0, 16)}…` : hash;
}

function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    month: 'short', day: '2-digit', hour: '2-digit', minute: '2-digit',
  });
}

// ─── Per-pillar proposal aggregation ────────────────────────────────────────
//
// Investigate/Correlate/Prevent have no earned-trust ladder the way Recover does — a Playbook
// Ledger entry here is a *proposal a human reviews*, never a grant of execution authority. This
// buckets each pillar's entries by lifecycle state so the page can show real counts without
// implying an autonomy level that doesn't exist for these three pillars. Pure client-side
// aggregation over the existing `GET /playbook/entries?pillarKind=` read — no new backend work.

interface PillarBucket {
  total: number;
  awaiting: number; // Proposed | UnderReview | Edited — a human hasn't decided yet
  approved: number; // Approved — a human agreed; for Prevent this means "currently active, ObserveOnly"
  revoked: number; // Revoked — an operator turned off a standing rule (Prevent only, today)
  closed: number; // Rejected | Expired | Superseded — decided against, or no decision was ever made
}

/**
 * How many Playbook Ledger entries the reasoning companion (W5, `services/agent`) actually
 * authored, across every pillar this page already fetches. Evidence, not configuration: this page
 * reads the ledgers rather than a feature flag, so the reasoning card says "no proposals recorded"
 * when there are none and shows the real count when there are — instead of the constant
 * "Not available yet" it carried before W5 shipped, which stayed false once an operator enabled it.
 */
function countReasoningProposals(...groups: (PlaybookEntry[] | undefined)[]): number {
  let count = 0;
  for (const entries of groups) {
    for (const entry of entries ?? []) {
      if (entry.proposerKind === 'ReasoningAgent') count++;
    }
  }
  return count;
}

function bucketPillarEntries(entries: PlaybookEntry[] | undefined): PillarBucket {
  const bucket: PillarBucket = { total: entries?.length ?? 0, awaiting: 0, approved: 0, revoked: 0, closed: 0 };
  for (const entry of entries ?? []) {
    if (entry.state === 'Proposed' || entry.state === 'UnderReview' || entry.state === 'Edited') bucket.awaiting++;
    else if (entry.state === 'Approved') bucket.approved++;
    else if (entry.state === 'Revoked') bucket.revoked++;
    else bucket.closed++; // Rejected, Expired, Superseded
  }
  return bucket;
}

function PillarCard({
  title, bucket, approvedLabel, href, isError,
}: {
  title: string;
  bucket: PillarBucket;
  /** What an "Approved" entry means for this pillar — differs for Prevent (a standing rule). */
  approvedLabel: string;
  href: string;
  /** The pillar's own fetch failed — show that honestly rather than rendering it as "empty." */
  isError?: boolean;
}) {
  return (
    <Link
      to={href}
      className="block bg-white border border-gray-200 rounded-lg p-4 shadow-sm hover:border-blue-300 hover:shadow transition-all"
    >
      <div className="text-sm font-semibold text-gray-800">{title}</div>
      {isError ? (
        <div className="text-xs text-red-600 mt-2">Couldn't load this pillar's proposals.</div>
      ) : bucket.total === 0 ? (
        <div className="text-xs text-gray-500 mt-2">No proposals recorded yet for this pillar.</div>
      ) : (
        <dl className="mt-2 space-y-1 text-xs">
          <div className="flex justify-between">
            <dt className="text-gray-500">Awaiting a human decision</dt>
            <dd className="font-semibold text-amber-700">{bucket.awaiting}</dd>
          </div>
          <div className="flex justify-between">
            <dt className="text-gray-500">{approvedLabel}</dt>
            <dd className="font-semibold text-green-700">{bucket.approved}</dd>
          </div>
          {bucket.revoked > 0 && (
            <div className="flex justify-between">
              <dt className="text-gray-500">Revoked by an operator</dt>
              <dd className="font-semibold text-gray-600">{bucket.revoked}</dd>
            </div>
          )}
          <div className="flex justify-between">
            <dt className="text-gray-500">Rejected, expired, or superseded</dt>
            <dd className="font-semibold text-gray-500">{bucket.closed}</dd>
          </div>
        </dl>
      )}
    </Link>
  );
}

/** One row of the "what's automatic vs. what waits for you" verb taxonomy (roadmap §4 / §7). */
function VerbCard({
  icon: Icon, iconClass, title, description, metric, href, unavailable,
}: {
  icon: React.ComponentType<{ className?: string }>;
  iconClass: string;
  title: string;
  description: string;
  /** A real, sourced number/label — omit rather than fabricate one. */
  metric?: string;
  href?: string;
  unavailable?: boolean;
}) {
  const content = (
    <div
      className={`h-full bg-white border rounded-lg p-4 shadow-sm ${
        unavailable ? 'border-dashed border-gray-300' : 'border-gray-200'
      } ${href ? 'hover:border-blue-300 hover:shadow transition-all' : ''}`}
    >
      <div className="flex items-center gap-2">
        <Icon className={`w-4 h-4 shrink-0 ${iconClass}`} />
        <div className="text-sm font-semibold text-gray-800">{title}</div>
      </div>
      <p className="text-xs text-gray-500 mt-1.5">{description}</p>
      {metric && <div className="text-xs font-semibold text-gray-800 mt-2">{metric}</div>}
      {unavailable && <div className="text-xs font-medium text-gray-500 mt-2">Not available yet</div>}
    </div>
  );
  return href ? <Link to={href}>{content}</Link> : content;
}

/**
 * `/autonomy` — how autonomous ServiceHub currently is, which capabilities operate automatically
 * versus wait for a human, what evidence and governance support that, and what an advanced user
 * can legitimately configure themselves (roadmap §4, §7, §11 item 5, §15 item 9). Renamed and
 * redesigned from the earlier "Autonomy Dashboard" (see docs-private/AUTONOMY-UX-2026-08-30.md):
 * deliberately "Autonomy," never "AI" — every fact this page shows is a deterministic,
 * evidence-based read from the Recovery Evidence Ledger, Playbook Ledger, and Governance grants;
 * no AI reasoning is in the execution path today (ADR-0005), and the one card describing a future
 * AI reasoning layer is intentionally marked unavailable rather than implied. Pure read-side
 * aggregation over existing endpoints — this page never itself grants, revokes, or otherwise
 * decides autonomy, and there is no "enable autonomy" control anywhere on it.
 */
export default function AutonomyPage() {
  const { isDemoMode, cloudProvider } = useDemoContext();
  const prefix = isDemoMode && cloudProvider ? `/demo/${cloudProvider}` : '';

  const { data: overview, isLoading, isError, refetch, isFetching } = useAutonomyDashboard();
  const { data: approvalQueue } = useApprovalQueue();
  const { data: capabilities } = useProviderCapabilities();
  const { data: me } = useMe();

  const investigate = usePlaybookEntries({ pillarKind: 'Investigate' });
  const correlate = usePlaybookEntries({ pillarKind: 'Correlate' });
  const prevent = usePlaybookEntries({ pillarKind: 'Prevent' });
  const recoverProposals = usePlaybookEntries({ pillarKind: 'Recover' });

  const investigateBucket = bucketPillarEntries(investigate.data);
  const correlateBucket = bucketPillarEntries(correlate.data);
  const preventBucket = bucketPillarEntries(prevent.data);
  const recoverBucket = bucketPillarEntries(recoverProposals.data);
  const reasoningProposalCount = countReasoningProposals(
    investigate.data, correlate.data, prevent.data, recoverProposals.data);

  const totalAwaitingReview =
    investigateBucket.awaiting + correlateBucket.awaiting + preventBucket.awaiting + recoverBucket.awaiting;

  const levelCounts = overview?.levelCounts ?? [];
  const grants = overview?.grants ?? [];
  const circuitBreakerTrips = overview?.circuitBreakerTrips ?? [];
  const recentTransitions = overview?.recentTransitions ?? [];
  const standingCount = levelCounts.filter(c => c.level >= 4).reduce((sum, c) => sum + c.count, 0);
  const approvalQueueCount = approvalQueue?.length ?? 0;

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      <div className="bg-white border-b border-gray-200 px-6 py-4 shrink-0">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-xl font-bold text-gray-900 flex items-center gap-2">
              <Gauge className="w-5 h-5 text-blue-600" />
              Autonomy
            </h1>
            <p className="text-sm text-gray-500 mt-0.5">
              How autonomous ServiceHub actually is right now, what's earned versus waiting on you,
              and why — read directly from the Recovery Evidence Ledger, the Playbook Ledger, and
              Governance. Deterministic and evidence-based, not AI: no reasoning model is in the
              execution path today, and nothing here can be switched on globally.
            </p>
          </div>
          <button
            onClick={() => refetch()}
            disabled={isFetching || isDemoMode}
            className="flex items-center gap-1.5 px-3 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-200 rounded-lg hover:bg-gray-50 shadow-sm transition-all disabled:opacity-50 shrink-0"
          >
            <RefreshCw className={`w-4 h-4 ${isFetching ? 'animate-spin' : ''}`} />
            Refresh
          </button>
        </div>

        {isDemoMode && (
          <div className="mt-3 flex items-center gap-2 px-3 py-2 rounded-lg bg-amber-50 border border-amber-200 text-xs text-amber-800">
            <Info className="w-4 h-4 shrink-0" />
            Demo Mode — there is no live ledger here, so evidence-backed sections below are
            honestly empty rather than fabricated. Provider constraints are real product facts and
            still shown.
          </div>
        )}

        {overview?.emergencyStopActive && (
          <div className="mt-3 flex items-center gap-2 px-3 py-2 rounded-lg bg-red-50 border border-red-200 text-xs text-red-800">
            <ShieldAlert className="w-4 h-4 shrink-0" />
            <span className="font-medium">Emergency stop is active</span> — no new unattended
            (Automation/System) execution can proceed for this owner until it's cleared.
          </div>
        )}
      </div>

      <div className="flex-1 overflow-auto p-6">
        {isLoading ? (
          <div className="flex items-center justify-center h-64">
            <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600" />
          </div>
        ) : isError ? (
          <div className="flex items-center justify-center h-64">
            <div className="text-center">
              <AlertCircle className="w-10 h-10 text-red-400 mx-auto mb-3" />
              <p className="text-gray-600 font-medium">Failed to load the autonomy overview</p>
              <button onClick={() => refetch()} className="mt-3 px-4 py-2 text-sm text-blue-600 hover:text-blue-700 border border-blue-300 rounded-lg hover:bg-blue-50">
                Try Again
              </button>
            </div>
          </div>
        ) : (
          <div className="space-y-8 max-w-6xl">

            {/* ── How autonomous is ServiceHub right now, per pillar ── */}
            <section>
              <h2 className="text-sm font-semibold text-gray-800 mb-3">How autonomous is ServiceHub right now</h2>
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
                <div className="bg-white border border-gray-200 rounded-lg p-4 shadow-sm">
                  <div className="text-sm font-semibold text-gray-800">Recover</div>
                  <div className="mt-2 flex items-baseline gap-2">
                    <span className="text-2xl font-bold text-green-700">{standingCount}</span>
                    <span className="text-xs text-gray-500">of {overview?.totalSignatures ?? 0} signatures at Standing/Unattended</span>
                  </div>
                  <div className="text-xs text-gray-500 mt-1">Earned trust, per signature — the only pillar with an execution-level ladder today.</div>
                  {recoverBucket.awaiting > 0 && (
                    <Link to={`${prefix}/playbook?pillar=Recover`} className="text-xs text-blue-600 hover:underline mt-2 inline-block">
                      +{recoverBucket.awaiting} replay proposal{recoverBucket.awaiting === 1 ? '' : 's'} awaiting review →
                    </Link>
                  )}
                </div>
                <PillarCard title="Investigate" bucket={investigateBucket} approvedLabel="Human agreed, sound" href={`${prefix}/playbook?pillar=Investigate`} isError={investigate.isError} />
                <PillarCard title="Correlate" bucket={correlateBucket} approvedLabel="Human agreed, sound" href={`${prefix}/playbook?pillar=Correlate`} isError={correlate.isError} />
                <PillarCard title="Prevent" bucket={preventBucket} approvedLabel="Active, ObserveOnly rules" href={`${prefix}/playbook?pillar=Prevent`} isError={prevent.isError} />
              </div>
            </section>

            {/* ── What's automatic vs. what waits for you ── */}
            <section>
              <h2 className="text-sm font-semibold text-gray-800 mb-3">What's automatic vs. what waits for you</h2>
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
                <VerbCard
                  icon={Eye}
                  iconClass="text-sky-500"
                  title="Automatic detection"
                  description="Dead-letter classification and drift findings run continuously — no approval needed to detect."
                  href={`${prefix}/dlq-history`}
                />
                <VerbCard
                  icon={Sparkles}
                  iconClass="text-indigo-500"
                  title="Recommendation / proposal"
                  description="A finding worth a human's attention, written to the Playbook Ledger. Never itself an action."
                  metric={totalAwaitingReview > 0 ? `${totalAwaitingReview} awaiting your decision` : undefined}
                  href={`${prefix}/playbook`}
                />
                <VerbCard
                  icon={CheckCircle2}
                  iconClass="text-amber-500"
                  title="Human-approved action"
                  description="A rule match the Eligibility Gate escalated, or a proposal a human signed off on."
                  metric={approvalQueueCount > 0 ? `${approvalQueueCount} replay decision${approvalQueueCount === 1 ? '' : 's'} waiting now` : 'Approval Queue is empty right now'}
                  href={`${prefix}/approval-queue`}
                />
                <VerbCard
                  icon={ShieldCheck}
                  iconClass="text-green-600"
                  title="Earned unattended execution"
                  description="A signature that has proven itself (Standing L4 / Unattended L5) — Recover pillar only, today."
                  metric={`${standingCount} signature${standingCount === 1 ? '' : 's'} currently earn this`}
                  href={`${prefix}/recovery`}
                />
                <VerbCard
                  icon={Lock}
                  iconClass="text-gray-500"
                  title="ObserveOnly prevention"
                  description="A standing Prevent-pillar rule. It can only ever record what it observed — never replay or purge."
                  metric={`${preventBucket.approved} active rule${preventBucket.approved === 1 ? '' : 's'}`}
                  href={`${prefix}/playbook?pillar=Prevent`}
                />
                <VerbCard
                  icon={ShieldQuestion}
                  iconClass={reasoningProposalCount > 0 ? 'text-purple-500' : 'text-gray-500'}
                  title="AI-suggested observation"
                  description="An optional, self-hosted reasoning companion reads this evidence and writes proposals into the Playbook Ledger for you to approve or reject. It can never execute, approve, or promote anything — the boundary is enforced by an IL scan, not by review (ADR-0005). Off by default."
                  metric={reasoningProposalCount > 0
                    ? `${reasoningProposalCount} proposal${reasoningProposalCount === 1 ? '' : 's'} recorded`
                    : 'Not enabled — no proposals recorded'}
                  href={reasoningProposalCount > 0 ? `${prefix}/playbook` : undefined}
                />
              </div>
            </section>

            {/* ── Provider constraints ── */}
            <section>
              <h2 className="text-sm font-semibold text-gray-800 mb-3 flex items-center gap-2">
                <Cloud className="w-4 h-4 text-blue-500" /> Provider constraints
              </h2>
              <div className="bg-white border border-gray-200 rounded-lg shadow-sm overflow-hidden">
                {!capabilities ? (
                  <div className="px-4 py-6 text-center text-sm text-gray-500">Loading provider capabilities…</div>
                ) : (
                  <table className="w-full text-sm">
                    <thead className="bg-gray-50 border-b border-gray-200">
                      <tr>
                        <th scope="col" className="px-4 py-2 text-left text-xs font-semibold text-gray-500">Provider</th>
                        <th scope="col" className="px-4 py-2 text-left text-xs font-semibold text-gray-500">Can prove DLQ absence</th>
                        <th scope="col" className="px-4 py-2 text-left text-xs font-semibold text-gray-500">Autonomy ceiling</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100">
                      {([
                        ['Azure', capabilities.Azure?.canProveDlqAbsence ?? false, capabilities.Azure?.notes],
                        ['AWS', capabilities.Aws?.canProveDlqAbsence ?? false, capabilities.Aws?.notes],
                        ['GCP', capabilities.Gcp?.canProveDlqAbsence ?? false, capabilities.Gcp?.notes],
                      ] as const).map(([label, canProve, notes]) => (
                        <tr key={label}>
                          <td className="px-4 py-2.5 font-medium text-gray-800">{label}</td>
                          <td className="px-4 py-2.5">
                            <span className={`text-xs font-medium px-2 py-0.5 rounded-full ${canProve ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-600'}`}>
                              {canProve ? 'Yes' : 'No'}
                            </span>
                          </td>
                          <td className="px-4 py-2.5 text-xs text-gray-600" title={notes}>
                            {canProve
                              ? 'Can reach Standing (L4) / Unattended (L5) once evidence supports it.'
                              : 'Permanently capped at Approve (L3) — a provider fact, not a maturity gap. Human approval is always required.'}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
              </div>
            </section>

            {/* ── Evidence & safety floors ── */}
            <section>
              <h2 className="text-sm font-semibold text-gray-800 mb-3">Evidence &amp; safety floors</h2>
              <div className="bg-white border border-gray-200 rounded-lg shadow-sm p-4 text-xs text-gray-600 space-y-2">
                <p><span className="font-semibold text-gray-700">Approve (L3) is a permanent floor</span> — every signature starts there and nothing skips it. It is not a rung to climb past.</p>
                <p><span className="font-semibold text-gray-700">L3 → L4 (Standing)</span> requires at least 10 verified outcomes at a 95%+ success rate. <span className="font-semibold text-gray-700">L4 → L5 (Unattended)</span> requires at least 30 at a 99%+ success rate. Two consecutive failures, or an operator's duplicate-business-effect flag, demotes immediately.</p>
                <p><span className="font-semibold text-gray-700">Trust is earned automatically from ledger evidence — it cannot be granted directly by any user, ever.</span> There is no control on this page, or anywhere in ServiceHub, that sets a signature's level.</p>
              </div>

              {circuitBreakerTrips.length > 0 && (
                <div className="mt-3 bg-white border border-red-200 rounded-lg shadow-sm">
                  <div className="px-4 py-3 border-b border-red-100 flex items-center gap-2">
                    <Zap className="w-4 h-4 text-red-500" />
                    <h3 className="text-sm font-semibold text-gray-800">Circuit-breaker-tripped rules</h3>
                  </div>
                  <ul className="divide-y divide-gray-100">
                    {circuitBreakerTrips.map((trip: CircuitBreakerTrip) => (
                      <li key={trip.ruleId} className="px-4 py-3 text-sm">
                        <div className="font-medium text-gray-800">{trip.ruleName}</div>
                        {trip.disabledReasonDetail && (
                          <div className="text-xs text-gray-500 mt-0.5">{trip.disabledReasonDetail}</div>
                        )}
                      </li>
                    ))}
                  </ul>
                </div>
              )}

              <div className="mt-3 bg-white border border-gray-200 rounded-lg shadow-sm">
                <div className="px-4 py-3 border-b border-gray-200 flex items-center gap-2">
                  <History className="w-4 h-4 text-gray-500" />
                  <h3 className="text-sm font-semibold text-gray-800">Recent promotions &amp; demotions</h3>
                </div>
                {recentTransitions.length === 0 ? (
                  <div className="px-4 py-6 text-center text-sm text-gray-500">
                    No promotion or demotion has been recorded for this owner yet.
                  </div>
                ) : (
                  <ul className="divide-y divide-gray-100">
                    {recentTransitions.map((t: AutonomyTransitionSummary, i) => (
                      <li key={i} className="px-4 py-3 text-sm flex items-start justify-between gap-3">
                        <div className="min-w-0">
                          <div className="flex items-center gap-1.5 font-mono text-xs text-gray-700" title={t.signatureHash}>
                            {truncateHash(t.signatureHash)}
                            <span className="text-gray-500 font-sans">({t.actionKind})</span>
                          </div>
                          <div className="text-xs text-gray-500 mt-1">
                            L{t.previousLevel} → L{t.newLevel} — {t.reason}
                          </div>
                        </div>
                        <div className="text-xs text-gray-500 whitespace-nowrap shrink-0">{formatDateTime(t.occurredAtUtc)}</div>
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            </section>

            {/* ── Signature standings (drill-down) ── */}
            <section>
              <h2 className="text-sm font-semibold text-gray-800 mb-3">Signature standings</h2>
              {levelCounts.length > 0 && (
                <div className="bg-white border border-gray-200 rounded-lg shadow-sm mb-3">
                  <div className="p-4 flex flex-wrap gap-3">
                    {levelCounts.map(c => (
                      <div key={`${c.actionKind}-${c.level}`} className="flex items-center gap-2 px-3 py-2 bg-gray-50 rounded-lg border border-gray-200">
                        <LevelBadge level={c.level} label={c.levelLabel} />
                        <span className="text-xs text-gray-500">{c.actionKind}</span>
                        <span className="text-sm font-semibold text-gray-800">{c.count}</span>
                      </div>
                    ))}
                  </div>
                </div>
              )}
              <div className="bg-white border border-gray-200 rounded-lg shadow-sm">
                {grants.length === 0 ? (
                  <div className="px-4 py-8 text-center text-sm text-gray-500">
                    No signature has ever been promoted past the Approve (L3) floor for this owner yet.
                  </div>
                ) : (
                  <table className="w-full text-sm" aria-label="Autonomy grants">
                    <thead className="bg-gray-50 border-b border-gray-200">
                      <tr>
                        <th scope="col" className="px-4 py-2 text-left text-xs font-semibold text-gray-500">Signature</th>
                        <th scope="col" className="px-4 py-2 text-left text-xs font-semibold text-gray-500">Action</th>
                        <th scope="col" className="px-4 py-2 text-left text-xs font-semibold text-gray-500">Level</th>
                        <th scope="col" className="px-4 py-2 text-left text-xs font-semibold text-gray-500">Updated</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100">
                      {grants.map((grant: AutonomyGrantSummary) => (
                        <tr key={`${grant.signatureHash}-${grant.actionKind}`} className="hover:bg-gray-50">
                          <td className="px-4 py-2.5 font-mono text-xs text-gray-700" title={grant.signatureHash}>
                            {truncateHash(grant.signatureHash)}
                          </td>
                          <td className="px-4 py-2.5 text-xs text-gray-600">{grant.actionKind}</td>
                          <td className="px-4 py-2.5">
                            <LevelBadge level={grant.currentLevel} label={grant.levelLabel} />
                          </td>
                          <td className="px-4 py-2.5 text-xs text-gray-500 whitespace-nowrap">{formatDateTime(grant.updatedAtUtc)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
              </div>
            </section>

            {/* ── Governance & what you can legitimately do ── */}
            <section>
              <h2 className="text-sm font-semibold text-gray-800 mb-3">Governance, and what you can configure yourself</h2>
              <div className="bg-white border border-gray-200 rounded-lg shadow-sm p-4">
                <div className="flex items-center justify-between flex-wrap gap-2">
                  <div className="text-xs text-gray-600">
                    Your fleet-wide Governance role:{' '}
                    <span className="font-semibold text-gray-800">{me?.governanceRole ?? 'Not activated for this owner'}</span>
                  </div>
                  <Link to={`${prefix}/governance`} className="text-xs text-blue-600 hover:underline shrink-0">
                    Manage grants →
                  </Link>
                </div>
                <ul className="mt-3 space-y-1.5 text-xs text-gray-600 list-disc list-inside">
                  <li>
                    <Link to={`${prefix}/rules`} className="text-blue-600 hover:underline">Create or enable an Auto-Replay Rule</Link> to opt a
                    signature class into evaluation — necessary, but not sufficient, for unattended execution.
                  </li>
                  <li>
                    <Link to={`${prefix}/governance`} className="text-blue-600 hover:underline">Grant or revoke Governance roles</Link> (Admin only) to
                    authorize who can approve, operate, or write off — this authorizes people, never an autonomy level.
                  </li>
                  <li>
                    <Link to={`${prefix}/playbook`} className="text-blue-600 hover:underline">Review and disposition Playbook proposals</Link> across
                    any pillar — approving one means "a human agrees this is sound," never itself a replay or purge.
                  </li>
                  <li>
                    What you <span className="font-semibold text-gray-700">cannot</span> do: set a signature's autonomy level directly, or
                    turn autonomy on globally. There is no such control, by design.
                  </li>
                </ul>
              </div>
            </section>

            <div className="flex items-center gap-1.5 pt-1 pb-6">
              <ClipboardList className="w-3.5 h-3.5 text-gray-300" />
              <span className="text-xs text-gray-500">
                Every number on this page is a live read from the Recovery Evidence Ledger, the Playbook Ledger, Governance grants, or a static
                provider capability fact — nothing is estimated or simulated.
              </span>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
