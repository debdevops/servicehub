import { Link } from 'react-router-dom';
import {
  GraduationCap,
  RefreshCw,
  Gauge,
  ShieldCheck,
  ClipboardList,
  Users,
  CheckCircle2,
  Cloud,
  Lock,
  Sparkles,
  ArrowRight,
} from 'lucide-react';
import { useDemoContext } from '@servicehub/ui-shared/lib/demo/DemoContext';

type BadgeKind = 'current' | 'bounded' | 'human' | 'future' | 'optional';

const BADGE_STYLES: Record<BadgeKind, { label: string; className: string }> = {
  current: { label: 'CURRENT', className: 'bg-emerald-100 text-emerald-700 border-emerald-300' },
  bounded: { label: 'BOUNDED', className: 'bg-blue-100 text-blue-700 border-blue-300' },
  human: { label: 'HUMAN REQUIRED', className: 'bg-amber-100 text-amber-800 border-amber-300' },
  future: { label: 'FUTURE', className: 'bg-gray-100 text-gray-600 border-gray-300' },
  // Shipped, but off unless an operator turns it on — distinct from both CURRENT (running now)
  // and FUTURE (not built). The reasoning companion is the only section that is genuinely this.
  optional: { label: 'OPT-IN', className: 'bg-purple-100 text-purple-700 border-purple-300' },
};

function Badge({ kind }: { kind: BadgeKind }) {
  const { label, className } = BADGE_STYLES[kind];
  return (
    <span
      className={`inline-flex items-center px-2 py-0.5 rounded-full text-[10px] font-bold tracking-wide border ${className}`}
    >
      {label}
    </span>
  );
}

function Section({
  id,
  title,
  badges,
  children,
}: {
  id: string;
  title: string;
  badges?: BadgeKind[];
  children: React.ReactNode;
}) {
  return (
    <section
      id={id}
      className="bg-white rounded-xl border border-gray-200 shadow-sm p-6 scroll-mt-4"
      aria-labelledby={`${id}-heading`}
    >
      <div className="flex items-center gap-2 flex-wrap mb-3">
        <h2 id={`${id}-heading`} className="text-base font-semibold text-gray-900">
          {title}
        </h2>
        {badges?.map((b) => (
          <Badge key={b} kind={b} />
        ))}
      </div>
      <div className="text-sm text-gray-700 leading-relaxed space-y-3">{children}</div>
    </section>
  );
}

function PillarCard({
  name,
  verb,
  rungs,
}: {
  name: string;
  verb: string;
  rungs: { label: string; badge: BadgeKind }[];
}) {
  return (
    <div className="rounded-lg border border-gray-200 p-4 bg-gray-50/60">
      <div className="flex items-center justify-between mb-2">
        <h3 className="text-sm font-semibold text-gray-900">{name}</h3>
        <span className="text-[11px] text-gray-500">{verb}</span>
      </div>
      <ul className="space-y-1">
        {rungs.map((r) => (
          <li key={r.label} className="flex items-center justify-between text-xs">
            <span className="text-gray-700">{r.label}</span>
            <Badge kind={r.badge} />
          </li>
        ))}
      </ul>
    </div>
  );
}

/**
 * The canonical, user-facing explanation of "Advanced ServiceHub" — what the Autonomy,
 * Recovery Evidence, Playbook Ledger, and Governance pages actually are, why they're grouped
 * together, and what ServiceHub's autonomy model does and does not mean. Purely static/
 * educational: no API calls, nothing here can drift out of sync with live data because it
 * never shows any — every specific number lives on the pages this one links to.
 *
 * Content is sourced from, and must stay consistent with, the master roadmap
 * (docs-private/SERVICEHUB-AUTONOMOUS-MASTER-ROADMAP-2026-08-27.md), the status doc
 * (docs-private/AUTONOMY-STATUS-AND-NEXT-STEPS-2026-08-29.md), and the ADRs (docs/adr/0001–0008).
 * If any of those change, this page's claims need re-checking against them, not the reverse.
 */
export function AdvancedServiceHubPage() {
  const { isDemoMode, cloudProvider } = useDemoContext();
  const prefix = isDemoMode && cloudProvider ? `/demo/${cloudProvider}` : '';

  return (
    <div className="h-full overflow-y-auto bg-gradient-to-b from-white via-blue-50 to-white">
      <div className="max-w-4xl mx-auto px-6 py-8 space-y-6">
        {/* Hero */}
        <div className="mb-2">
          <div className="flex items-center gap-4 mb-3">
            <div className="w-14 h-14 bg-gradient-to-br from-blue-500 to-indigo-600 rounded-2xl flex items-center justify-center shadow-lg shrink-0">
              <GraduationCap className="w-7 h-7 text-white" />
            </div>
            <div>
              <h1 className="text-3xl font-bold text-gray-900">Advanced ServiceHub</h1>
              <p className="text-sm text-gray-500 mt-1">
                What ServiceHub's autonomy actually is, in plain language — no source reading required
              </p>
            </div>
          </div>
          <p className="text-gray-600 ml-[68px] text-sm leading-relaxed max-w-2xl">
            This page explains the architecture and operational model behind the{' '}
            <span className="font-medium text-gray-800">Advanced ServiceHub</span> section of Quick
            Access — Autonomy, Recovery Evidence, Playbook Ledger, and Governance. It is written for
            an operator who already uses ServiceHub and wants to understand how much of it runs
            unattended, why, and where the human floor still is.
          </p>
        </div>

        {/* Legend */}
        <div className="bg-white rounded-xl border border-gray-200 shadow-sm p-5">
          <h2 className="text-sm font-semibold text-gray-900 mb-3">How to read this page</h2>
          <div className="grid grid-cols-1 sm:grid-cols-4 gap-3 text-xs">
            <div className="flex items-start gap-2">
              <Badge kind="current" />
              <span className="text-gray-600">Implemented and operating today.</span>
            </div>
            <div className="flex items-start gap-2">
              <Badge kind="bounded" />
              <span className="text-gray-600">
                Available only once evidence, governance, or a safety gate allows it.
              </span>
            </div>
            <div className="flex items-start gap-2">
              <Badge kind="human" />
              <span className="text-gray-600">Requires a human's approval or action, by design.</span>
            </div>
            <div className="flex items-start gap-2">
              <Badge kind="future" />
              <span className="text-gray-600">Not implemented. Deliberately gated, not hidden.</span>
            </div>
          </div>
        </div>

        {/* 1-2: What & why */}
        <Section id="what-why" title="1–2. What Advanced ServiceHub means, and why it exists" badges={['current']}>
          <p>
            Every ServiceHub deployment has two kinds of pages. Most of them — Messages, DLQ
            Intelligence, Auto-Replay Rules, Cloud Bridge — are where you <em>use</em> ServiceHub day
            to day. <strong>Advanced ServiceHub</strong> is different: it is where ServiceHub explains
            and governs <em>itself</em> — how confident it is in each thing it can do unattended, what
            evidence backs that confidence, and who is allowed to change it.
          </p>
          <p>
            It exists because ServiceHub's mission — <em>Investigate → Recover → Prove it happened →
            Prevent the repeat</em> — only stays trustworthy if trust itself is visible. A system that
            silently replays messages without evidence a human can check is a liability, not
            automation. Advanced ServiceHub is the visibility and control layer that makes unattended
            action defensible: every claim it makes traces back to a specific record, not a vibe.
          </p>
        </Section>

        {/* 3: autonomy model / the loop */}
        <Section id="loop" title="3. The ServiceHub autonomy model" badges={['current']}>
          <p>
            ServiceHub's autonomy is organized as one continuous loop, not a single "autonomy level."
            Each stage matures independently, and only one of them — Recover — ever reaches an
            execution rung:
          </p>
          <div className="flex items-center gap-1.5 flex-wrap text-xs font-medium bg-gray-50 border border-gray-200 rounded-lg px-4 py-3">
            <RefreshCw className="w-3.5 h-3.5 text-gray-400 shrink-0" />
            {['Observe', 'Investigate', 'Correlate', 'Recover', 'Prove', 'Learn', 'Prevent'].map((stage, i, arr) => (
              <span key={stage} className="flex items-center gap-1.5">
                <span className="px-2 py-1 rounded bg-white border border-gray-200 text-gray-700">{stage}</span>
                {i < arr.length - 1 && <ArrowRight className="w-3 h-3 text-gray-300" />}
              </span>
            ))}
          </div>
          <p>
            Two rules hold this together, unconditionally:
          </p>
          <ul className="list-disc pl-5 space-y-1">
            <li>
              <strong>Evidence over confidence.</strong> Trust is a trailing function of verified
              outcomes an independent process observed after the fact — never of how confident
              anything was beforehand.
            </li>
            <li>
              <strong>Nouns over verbs.</strong> Reasoning (observation, correlation, narration,
              proposal) may grow without limit. Execution does not grow a third verb — replay and
              purge are the only two mutating operations ServiceHub has, today or in any roadmap item.
            </li>
          </ul>
        </Section>

        {/* 4-7: four pillars */}
        <Section
          id="pillars"
          title="4–7. The four pillars: Recover, Investigate, Correlate, Prevent"
          badges={['current', 'bounded']}
        >
          <p>
            Recover is the only pillar with an execution ladder (L0–L5) — because it is the only
            pillar that touches live queues. The other three top out at maximally-surfaced,
            unprompted, evidence-backed <em>observation</em>: they can tell you something, loudly and
            automatically, but none of them can act on it.
          </p>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 mt-2">
            <PillarCard
              name="Recover"
              verb="replay / purge"
              rungs={[
                { label: 'L0–L2 Observe, Explain, Recommend', badge: 'current' },
                { label: 'L3 Approve — permanent human floor', badge: 'human' },
                { label: 'L4 Standing (pre-approved, budget-bounded)', badge: 'bounded' },
                { label: 'L5 Unattended (tighter demotion sensitivity)', badge: 'bounded' },
              ]}
            />
            <PillarCard
              name="Investigate"
              verb="never acts, only surfaces"
              rungs={[
                { label: 'Observe, Classify, Trend', badge: 'current' },
                { label: 'Anomalize — deterministic stats, no ML', badge: 'current' },
                { label: 'Narrate — plain-English pattern summary', badge: 'current' },
                { label: 'Push — via webhook/SSE, unprompted', badge: 'current' },
              ]}
            />
            <PillarCard
              name="Correlate"
              verb="never acts, only links"
              rungs={[
                { label: 'Manual trace (correlation-ID search)', badge: 'current' },
                { label: 'Same-provider proactive correlation', badge: 'current' },
                { label: 'Cross-cloud proactive correlation', badge: 'current' },
                { label: 'External-signal (deploy/config) correlation', badge: 'current' },
              ]}
            />
            <PillarCard
              name="Prevent"
              verb="detects and reports, never acts"
              rungs={[
                { label: 'Baseline the good / drift detection', badge: 'current' },
                { label: 'Producer-facing contract-violation export', badge: 'current' },
                { label: 'Predictive backlog signal', badge: 'current' },
                { label: 'Prevention rules — ObserveOnly (see §13)', badge: 'bounded' },
              ]}
            />
          </div>
        </Section>

        {/* 8: Recovery Evidence Ledger */}
        <Section id="evidence-ledger" title="8. Evidence and the Recovery Evidence Ledger" badges={['current']}>
          <p>
            Every replay and purge ServiceHub ever executes is written to a hash-chained,
            append-only ledger before anything else happens. The chain is built so that a second
            operator, or an external auditor, can independently re-derive <em>why</em> the system
            trusted what it trusted — with zero server access, using only an exported ledger file.
            Nothing can edit or delete a past entry without breaking the chain in a way that's
            trivially detectable.
          </p>
          <Link
            to={`${prefix}/recovery`}
            className="inline-flex items-center gap-1.5 text-blue-600 hover:underline text-sm font-medium"
          >
            Open Recovery Evidence <ArrowRight className="w-3.5 h-3.5" />
          </Link>
        </Section>

        {/* 9: Playbook Ledger */}
        <Section id="playbook-ledger" title="9. Playbook Ledger and human disposition" badges={['current']}>
          <p>
            The Recovery Evidence Ledger records what ServiceHub <em>did</em>. The Playbook Ledger
            records what ServiceHub <em>proposed</em> — a plan, an anomaly flag, a correlation
            hypothesis, or a drift finding — across all four pillars, plus a human's disposition of
            it: approved as-is, edited then approved, rejected, or expired unactioned. It is the audit
            substrate for everything "learn" means beyond the Recover ladder itself, and it is the
            <em> only</em> surface any future reasoning layer will ever be allowed to write to (see
            §20).
          </p>
          <Link
            to={`${prefix}/playbook`}
            className="inline-flex items-center gap-1.5 text-blue-600 hover:underline text-sm font-medium"
          >
            Open Playbook Ledger <ArrowRight className="w-3.5 h-3.5" />
          </Link>
        </Section>

        {/* 10-11: Governance/RBAC, approval boundaries */}
        <Section id="governance" title="10–11. Governance / RBAC and approval boundaries" badges={['current', 'human']}>
          <p>
            Governance is per-namespace and per-pillar, not a single admin toggle. Roles
            (<strong>Admin</strong>, <strong>Operator</strong>, <strong>Approver</strong>) are granted
            to individual users scoped to a namespace and a pillar, and enforced on every mutating
            endpoint: replay/purge requires Operator on Recover, disposing a Playbook proposal
            requires Approver on that entry's pillar, granting/revoking roles requires Admin.
          </p>
          <p>
            <strong>L3 (Approve) is the permanent human floor</strong> for anything the evidence
            hasn't earned yet — not a rung a signature climbs past, and never bypassable by anything
            in the system, including a future reasoning layer. The Approval Queue is where that floor
            becomes a fast workflow instead of a manual hunt: one place to bulk-approve
            escalation-declined, rule-matched entries.
          </p>
          <div className="flex flex-wrap gap-4 pt-1">
            <Link to={`${prefix}/governance`} className="inline-flex items-center gap-1.5 text-blue-600 hover:underline text-sm font-medium">
              Open Governance <ArrowRight className="w-3.5 h-3.5" />
            </Link>
            <Link to={`${prefix}/approval-queue`} className="inline-flex items-center gap-1.5 text-blue-600 hover:underline text-sm font-medium">
              Open Approval Queue <ArrowRight className="w-3.5 h-3.5" />
            </Link>
          </div>
        </Section>

        {/* 12: provider limits */}
        <Section id="provider-limits" title="12. Provider-specific limits" badges={['bounded']}>
          <p>
            Azure Service Bus can prove a dead-letter queue is truly empty after a replay
            (<code className="text-xs bg-gray-100 px-1 py-0.5 rounded">CanProveDlqAbsence = true</code>),
            so it can earn the full L0–L5 ladder. AWS SQS/SNS and GCP Pub/Sub currently cannot make
            that same proof — a structural observability fact about those providers' preview
            integrations, confirmed and re-confirmed against the tree, <strong>not a maturity gap
            more learning closes</strong>. They are permanently capped at L3: fast, well-informed
            human approval, never zero-click. The only lever that changes this is real provider
            infrastructure (for example, an AWS SNS fan-out plus an observer queue) — out of scope
            for a code-only change, and never a trust-model relaxation.
          </p>
        </Section>

        {/* 13: ObserveOnly prevention */}
        <Section id="observe-only" title="13. ObserveOnly prevention" badges={['bounded']}>
          <p>
            Prevention rules let an operator define a condition over drift/anomaly findings (for
            example, "three drift findings on this entity within 24 hours"). When a rule's condition
            is met, ServiceHub proposes it into the Playbook Ledger — but the proposed action is
            always, unconditionally, <code className="text-xs bg-gray-100 px-1 py-0.5 rounded">ObserveOnly</code>.
            A prevention rule can flag a pattern for a human to review. It cannot pause a producer,
            block a queue, or take any action of its own — that would be a new verb, and Prevent has
            no execution rung, by the same design that keeps Investigate and Correlate observation-only.
          </p>
        </Section>

        {/* 14: how autonomy is earned */}
        <Section id="how-earned" title="14. How autonomy is earned" badges={['current']}>
          <p>
            Trust is keyed per <code className="text-xs bg-gray-100 px-1 py-0.5 rounded">(Owner, SignatureHash, ActionKind)</code>
            {' '}— a specific failure pattern, never a fleet, never a global setting. Promotion is a
            function of verified outcomes an independent worker observed after the fact:
          </p>
          <ul className="list-disc pl-5 space-y-1">
            <li><strong>L3 → L4:</strong> at least 10 verified outcomes, at least 95% success.</li>
            <li><strong>L4 → L5:</strong> at least 30 verified outcomes, at least 99% success.</li>
            <li>
              <strong>Demotion</strong> on two consecutive verified failures, or a
              duplicate-business-effect flag — permanent until a human lifts it.
            </li>
            <li>
              A non-configurable <strong>circuit breaker</strong> (last 20 verified outcomes, 50%
              success floor) and an owner-scoped <strong>emergency stop</strong> are checked ahead of
              every other rule, unconditionally.
            </li>
          </ul>
          <p>No amount of "looking confident" substitutes for this. The scoring never reads a model's stated certainty — only what actually happened.</p>
        </Section>

        {/* 15-17: what's automatic, what needs a human, what autonomous does NOT mean */}
        <Section id="today" title="15–16. What runs automatically today, and what still waits for you" badges={['current', 'human']}>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div>
              <h3 className="text-sm font-semibold text-gray-900 mb-2 flex items-center gap-1.5">
                <CheckCircle2 className="w-4 h-4 text-emerald-500" /> Automatic today
              </h3>
              <ul className="list-disc pl-5 space-y-1 text-sm">
                <li>Detecting, classifying, and trending every DLQ failure</li>
                <li>Deterministic anomaly, drift, and predictive-backlog detection</li>
                <li>Same-provider, cross-cloud, and external-signal correlation</li>
                <li>Plain-English narration and unprompted push (webhook/SSE)</li>
                <li>Producer-facing contract-violation exports</li>
                <li>Replay/purge — unattended, only where a signature has earned L4/L5 on Azure</li>
              </ul>
            </div>
            <div>
              <h3 className="text-sm font-semibold text-gray-900 mb-2 flex items-center gap-1.5">
                <Users className="w-4 h-4 text-amber-600" /> Still requires a human
              </h3>
              <ul className="list-disc pl-5 space-y-1 text-sm">
                <li>Every L3-and-below signature, and every AWS/GCP signature, always</li>
                <li>Disposing a Playbook proposal (approve, edit-approve, reject)</li>
                <li>Granting or revoking a Governance role</li>
                <li>Attesting an unsafe or duplicate-business-effect outcome (open to anyone, by design — see below)</li>
                <li>Any cross-pillar judgment call ("is this one incident or two")</li>
              </ul>
            </div>
          </div>
          <p className="text-xs text-gray-500 pt-1">
            One deliberate asymmetry: writing off a ledger entry requires the Operator role, but
            flagging an outcome unsafe does not — restricting who can report a problem would work
            against safety, not for it.
          </p>
        </Section>

        <Section id="not-autonomous" title="17. What “autonomous” does NOT mean" badges={['future']}>
          <ul className="list-disc pl-5 space-y-1 text-sm">
            <li>Not a system that invents new action types — replay and purge stay the only two, permanently.</li>
            <li>Not model-driven execution — nothing here is decided by an LLM's confidence, anywhere.</li>
            <li>Not zero human touch everywhere — L3 and the AWS/GCP ceiling are permanent by design, not gaps.</li>
            <li>Not cross-cloud coordinated action — correlation across providers, never coordinated execution across them.</li>
            <li>Not a system that reasons about anything it cannot independently prove happened.</li>
          </ul>
        </Section>

        {/* 18-19: no global switch, how to legitimately increase automation */}
        <Section id="no-switch" title="18–19. Why there is no global “Enable Autonomous” switch" badges={['current']}>
          <p>
            Per-signature earned trust is the entire point. A single global switch would mean trusting
            every failure pattern — proven and unproven alike — equally, which is exactly the
            confidence-over-evidence shortcut ServiceHub's architecture exists to refuse. This is
            listed explicitly, in the roadmap, among the things that must never be built.
          </p>
          <p>There are exactly three things an operator can legitimately do to increase how much runs unattended, and none of them sets an autonomy level directly:</p>
          <ol className="list-decimal pl-5 space-y-1">
            <li>
              <Link to={`${prefix}/rules`} className="text-blue-600 hover:underline font-medium">Create or enable an Auto-Replay Rule</Link>
              {' '}— opts a signature class into the Eligibility Gate's evaluation. Necessary, not sufficient, for unattended execution.
            </li>
            <li>
              <Link to={`${prefix}/governance`} className="text-blue-600 hover:underline font-medium">Grant or revoke Governance roles</Link>
              {' '}(Admin only) — authorizes people, never an autonomy level.
            </li>
            <li>
              <Link to={`${prefix}/playbook`} className="text-blue-600 hover:underline font-medium">Review and disposition Playbook proposals</Link>
              {' '}— "a human agrees this is sound," never itself a replay or purge.
            </li>
          </ol>
          <p>The level itself only ever moves because <code className="text-xs bg-gray-100 px-1 py-0.5 rounded">AutonomyEvaluationWorker</code> observed enough verified evidence to justify it (§14).</p>
        </Section>

        {/* 20-21: Reasoning Companion */}
        <Section id="reasoning-companion" title="20–21. The Reasoning Companion, and what it can and cannot do" badges={['optional']}>
          <p>
            ServiceHub ships an optional bounded reasoning layer
            (<code className="text-xs bg-gray-100 px-1 py-0.5 rounded">services/agent</code>, a
            sibling to <code className="text-xs bg-gray-100 px-1 py-0.5 rounded">services/ai</code>) —
            <strong> disabled by default</strong>, and inert even when enabled unless you also point
            it at a local model of your own. Its only interface to the rest of ServiceHub is: read
            evidence from any pillar, write a proposal into the Playbook Ledger for a human to
            approve or reject. It never executes, promotes, or confirms anything itself, and it
            never touches the Recovery Evidence Ledger — that boundary is enforced by an IL scan
            over the compiled assemblies, so a future change that tried to cross it fails the build
            rather than a review. It only ever sees counts, lifecycle state and already-normalised
            error terms, never a message body.
          </p>
          <p>
            Local-only is the permanent default posture: a self-hosted model on your own network is
            the only backend it knows how to talk to. An external-LLM opt-in would be a real
            amendment to ADR-0004's no-external-calls security model, requiring the same disclosure
            rigor as any other security-boundary change — never a config flag slipped in alongside a
            feature — and is deliberately not built.
          </p>
          <p>
            Everything else this page describes — the four-pillar loop, the Recovery and Playbook
            ledgers, Governance/RBAC, provider limits, and the earned-trust ladder — is
            deterministic, with no model anywhere in it. No autonomy level has ever moved because
            something inferred it: promotion and demotion are arithmetic over verified outcomes, and
            a reasoning proposal is a suggestion in a queue, not an input to that arithmetic.
          </p>
          <div className="flex items-center gap-2 text-xs text-gray-500 pt-1">
            <Sparkles className="w-3.5 h-3.5" />
            <span>The Autonomy page shows how many proposals the companion has actually recorded — and says "Not enabled" when there are none, rather than implying it doesn't exist.</span>
          </div>
        </Section>

        {/* Why these four pages are grouped */}
        <Section id="why-grouped" title="Why Autonomy, Recovery Evidence, Playbook Ledger, and Governance are grouped">
          <p>
            These four pages answer one question from four angles: <em>how much of ServiceHub is
            trusted to act on its own, and on what basis?</em> Autonomy shows the current standing
            per pillar. Recovery Evidence shows the immutable record of what actually executed.
            Playbook Ledger shows what was proposed and how a human dispositioned it. Governance
            shows who is authorized to change any of the above. None of the four make sense read in
            isolation — that's the reason they live together in Quick Access, separate from the daily
            Diagnose &amp; Automate workflow, rather than scattered under "Platform."
          </p>
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 pt-2">
            <Link to={`${prefix}/autonomy`} className="flex flex-col items-center gap-1.5 p-3 rounded-lg border border-gray-200 hover:border-blue-300 hover:bg-blue-50 transition-colors text-center">
              <Gauge className="w-5 h-5 text-blue-500" />
              <span className="text-xs font-medium text-gray-700">Autonomy</span>
            </Link>
            <Link to={`${prefix}/recovery`} className="flex flex-col items-center gap-1.5 p-3 rounded-lg border border-gray-200 hover:border-teal-300 hover:bg-teal-50 transition-colors text-center">
              <ShieldCheck className="w-5 h-5 text-teal-500" />
              <span className="text-xs font-medium text-gray-700">Recovery Evidence</span>
            </Link>
            <Link to={`${prefix}/playbook`} className="flex flex-col items-center gap-1.5 p-3 rounded-lg border border-gray-200 hover:border-indigo-300 hover:bg-indigo-50 transition-colors text-center">
              <ClipboardList className="w-5 h-5 text-indigo-500" />
              <span className="text-xs font-medium text-gray-700">Playbook Ledger</span>
            </Link>
            <Link to={`${prefix}/governance`} className="flex flex-col items-center gap-1.5 p-3 rounded-lg border border-gray-200 hover:border-red-300 hover:bg-red-50 transition-colors text-center">
              <Users className="w-5 h-5 text-red-500" />
              <span className="text-xs font-medium text-gray-700">Governance</span>
            </Link>
          </div>
          <div className="grid grid-cols-2 sm:grid-cols-3 gap-3 pt-1">
            <Link to={`${prefix}/approval-queue`} className="flex items-center gap-2 p-2.5 rounded-lg border border-gray-200 hover:border-amber-300 hover:bg-amber-50 transition-colors text-xs font-medium text-gray-700">
              <CheckCircle2 className="w-4 h-4 text-amber-500 shrink-0" /> Approval Queue
            </Link>
            <Link to={`${prefix}/insights`} className="flex items-center gap-2 p-2.5 rounded-lg border border-gray-200 hover:border-blue-300 hover:bg-blue-50 transition-colors text-xs font-medium text-gray-700">
              <Sparkles className="w-4 h-4 text-blue-500 shrink-0" /> Proactive Insights
            </Link>
            <Link to={`${prefix}/rules`} className="flex items-center gap-2 p-2.5 rounded-lg border border-gray-200 hover:border-amber-300 hover:bg-amber-50 transition-colors text-xs font-medium text-gray-700">
              <Cloud className="w-4 h-4 text-amber-500 shrink-0" /> Auto-Replay Rules
            </Link>
          </div>
        </Section>

        <div className="flex items-start gap-3 bg-gray-50 border border-gray-200 rounded-xl p-4 text-xs text-gray-500">
          <Lock className="w-4 h-4 shrink-0 mt-0.5 text-gray-400" />
          <p>
            This page is educational, not marketing — it describes only what the current codebase and
            its ADRs actually implement. It does not carry autonomy levels, readiness scores, or AI
            capabilities that don't exist yet. If something here looks out of date against what a page
            it links to actually shows, the linked page's live data is the source of truth.
          </p>
        </div>
      </div>
    </div>
  );
}
