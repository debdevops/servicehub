import type { PillarKind, PlaybookEntry, PlaybookEntryState } from '@servicehub/ui-shared/lib/api/playbook';
import type { Tone } from './ui';

/**
 * Pure, plain-language views of Playbook Ledger entries. Detection workers store their proposal
 * and evidence as JSON (see AnomalyDetectionWorker, DriftDetectionWorker, CorrelationDetectionWorker,
 * AutoReplayExecutor, PreventionRuleEvaluationService, ReasoningCompanionWorker); this turns the
 * known shapes into a one-line title an operator can read without opening the JSON, and falls back
 * to the proposal kind — never a guess — for anything it doesn't recognize.
 */

export function parseJsonObject(raw: string | null | undefined): Record<string, unknown> | null {
  if (!raw) return null;
  try {
    const parsed: unknown = JSON.parse(raw);
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? (parsed as Record<string, unknown>) : null;
  } catch {
    return null;
  }
}

/** Case-insensitive property read — backend payloads are PascalCase or camelCase depending on the writer. */
function pick(obj: Record<string, unknown> | null, key: string): unknown {
  if (!obj) return undefined;
  if (key in obj) return obj[key];
  const lower = key.toLowerCase();
  const match = Object.keys(obj).find(k => k.toLowerCase() === lower);
  return match ? obj[match] : undefined;
}

function str(obj: Record<string, unknown> | null, key: string): string | null {
  const v = pick(obj, key);
  return typeof v === 'string' && v.trim() ? v : typeof v === 'number' ? String(v) : null;
}

function num(obj: Record<string, unknown> | null, key: string): number | null {
  const v = pick(obj, key);
  return typeof v === 'number' && Number.isFinite(v) ? v : null;
}

/** `DlqGrowthSpike` → `DLQ growth spike`. */
export function humanizeIdentifier(value: string): string {
  const words = value
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2')
    .replace(/[_-]+/g, ' ')
    .trim()
    .split(/\s+/);
  return words
    .map((w, i) => {
      if (/^dlq$/i.test(w)) return 'DLQ';
      return i === 0 ? w.charAt(0).toUpperCase() + w.slice(1).toLowerCase() : w.toLowerCase();
    })
    .join(' ');
}

export const PROPOSAL_KIND_LABELS: Record<string, string> = {
  AnomalyFlag: 'Anomaly',
  DriftFinding: 'Drift finding',
  CorrelationHypothesis: 'Correlation',
  ReplayPlan: 'Replay plan',
  PreventionRuleProposal: 'Prevention rule',
  PreventionTrigger: 'Rule match',
  ReasoningCompanionObservation: 'AI observation',
};

export function proposalKindLabel(kind: string): string {
  return PROPOSAL_KIND_LABELS[kind] ?? humanizeIdentifier(kind);
}

export interface ProposalSummary {
  title: string;
  detail: string | null;
  /** Detection-worker severity, 0-100, when the proposal carries one. Never a confidence score. */
  severity: number | null;
  /** Suggested next actions the detection worker itself recorded, verbatim. */
  recommendedActions: string[];
}

export function summarizeProposal(entry: Pick<PlaybookEntry, 'proposalKind' | 'proposalJson'>): ProposalSummary {
  const p = parseJsonObject(entry.proposalJson);
  const recommendedRaw = pick(p, 'RecommendedActions');
  const recommendedActions = Array.isArray(recommendedRaw) ? recommendedRaw.filter((v): v is string => typeof v === 'string') : [];
  const severity = num(p, 'Severity');
  const entity = str(p, 'EntityName');

  switch (entry.proposalKind) {
    case 'AnomalyFlag':
    case 'DriftFinding': {
      const type = str(p, 'Type');
      const label = type ? humanizeIdentifier(type) : entry.proposalKind === 'AnomalyFlag' ? 'Anomaly' : 'Drift';
      return {
        title: entity ? `${label} on ${entity}` : label,
        detail: str(p, 'Description'),
        severity,
        recommendedActions,
      };
    }
    case 'CorrelationHypothesis': {
      const members = pick(p, 'Members');
      const providers = pick(p, 'Providers');
      const memberList = Array.isArray(members) ? (members as Record<string, unknown>[]) : [];
      const providerList = Array.isArray(providers) ? providers.filter((v): v is string => typeof v === 'string') : [];
      const entities = Array.from(new Set(memberList.map(m => str(m, 'EntityName')).filter((v): v is string => !!v)));
      const across = providerList.length > 1 ? ` across ${providerList.join(' and ')}` : providerList.length === 1 ? ` on ${providerList[0]}` : '';
      return {
        title: memberList.length > 0 ? `${memberList.length} related failures${across}` : 'Related failures',
        detail: entities.length > 0 ? `Involves ${entities.slice(0, 3).join(', ')}${entities.length > 3 ? ` and ${entities.length - 3} more` : ''}.` : null,
        severity,
        recommendedActions,
      };
    }
    case 'ReplayPlan': {
      const rule = str(p, 'RuleName');
      const messageId = str(p, 'MessageId');
      return {
        title: entity ? `Replay a message on ${entity}` : 'Replay a message',
        detail: [rule ? `Rule “${rule}” matched` : null, messageId ? `message ${messageId}` : null].filter(Boolean).join(' · ') || null,
        severity,
        recommendedActions,
      };
    }
    case 'PreventionRuleProposal': {
      const name = str(p, 'Name');
      return {
        title: name ? `Prevention rule: ${name}` : 'Prevention rule',
        detail: entity ? `Would watch ${entity} and record when the pattern recurs.` : null,
        severity,
        recommendedActions,
      };
    }
    case 'PreventionTrigger': {
      const name = str(p, 'Name');
      const occurrences = num(p, 'OccurrencesInWindow');
      const hours = num(p, 'WindowHours');
      const min = num(p, 'MinOccurrences');
      return {
        title: `${name ? `“${name}”` : 'A prevention rule'} matched${entity ? ` on ${entity}` : ''}`,
        detail:
          occurrences != null && hours != null
            ? `${occurrences} occurrence${occurrences === 1 ? '' : 's'} in ${hours}h${min != null ? ` (threshold ${min})` : ''}.`
            : null,
        severity: num(p, 'FindingSeverity') ?? severity,
        recommendedActions,
      };
    }
    case 'ReasoningCompanionObservation': {
      const summary = str(p, 'Summary');
      return { title: summary ?? 'AI observation', detail: null, severity, recommendedActions };
    }
    default:
      return { title: proposalKindLabel(entry.proposalKind), detail: str(p, 'Description'), severity, recommendedActions };
  }
}

/** Flattens a JSON payload to readable label/value rows for the detail panel's evidence section. */
export function toReadableRows(raw: string | null | undefined): Array<{ label: string; value: string }> {
  const obj = parseJsonObject(raw);
  if (!obj) return [];
  const rows: Array<{ label: string; value: string }> = [];
  for (const [key, value] of Object.entries(obj)) {
    if (value === null || value === undefined || value === '') continue;
    let display: string;
    if (Array.isArray(value)) {
      if (value.length === 0) continue;
      display = value.every(v => typeof v !== 'object') ? value.join(', ') : `${value.length} item${value.length === 1 ? '' : 's'}`;
    } else if (typeof value === 'object') {
      display = JSON.stringify(value);
    } else if (typeof value === 'string' && /^\d{4}-\d{2}-\d{2}T/.test(value)) {
      display = new Date(value).toLocaleString();
    } else {
      display = String(value);
    }
    rows.push({ label: humanizeIdentifier(key), value: display });
  }
  return rows;
}

// ─── Pillars, states, stats ─────────────────────────────────────────────────

export const PILLAR_ORDER: readonly PillarKind[] = ['Investigate', 'Correlate', 'Prevent', 'Recover'];

export const PILLAR_META: Record<PillarKind, { question: string; tone: Tone; explainer: string }> = {
  Investigate: {
    question: 'What looks unusual?',
    tone: 'blue',
    explainer: 'Anomalies a detection worker spotted in one namespace — spikes, stalls, unusual failure patterns.',
  },
  Correlate: {
    question: 'Which failures are related?',
    tone: 'violet',
    explainer: 'Hypotheses that failures in different places share a cause — possibly across clouds.',
  },
  Prevent: {
    question: 'What could stop this recurring?',
    tone: 'teal',
    explainer: 'Drift findings and prevention rules. Prevention rules are observe-only: they record matches and never act.',
  },
  Recover: {
    question: 'Which replays need a human?',
    tone: 'amber',
    explainer: 'Replay plans an Auto-Replay Rule could not run on its own. Approving here does not replay anything.',
  },
};

export type StateGroup = 'awaiting' | 'approved' | 'rejected' | 'closed';

export function stateGroup(state: PlaybookEntryState): StateGroup {
  switch (state) {
    case 'Proposed':
    case 'UnderReview':
    case 'Edited':
      return 'awaiting';
    case 'Approved':
      return 'approved';
    case 'Rejected':
      return 'rejected';
    default:
      return 'closed'; // Expired, Superseded, Revoked
  }
}

export const STATE_META: Record<PlaybookEntryState, { label: string; tone: Tone }> = {
  Proposed: { label: 'Awaiting decision', tone: 'amber' },
  UnderReview: { label: 'Under review', tone: 'blue' },
  Edited: { label: 'Edited', tone: 'violet' },
  Approved: { label: 'Approved', tone: 'green' },
  Rejected: { label: 'Rejected', tone: 'red' },
  Expired: { label: 'Expired', tone: 'gray' },
  Superseded: { label: 'Superseded', tone: 'gray' },
  Revoked: { label: 'Revoked', tone: 'gray' },
};

export interface PlaybookStats {
  total: number;
  awaiting: number;
  approved: number;
  rejected: number;
  closed: number;
  aiAuthored: number;
  /** approved ÷ (approved + rejected); `null` until a human has decided at least one. */
  approvalRate: number | null;
  byPillar: Record<PillarKind, number>;
}

export function computePlaybookStats(entries: readonly PlaybookEntry[]): PlaybookStats {
  const stats: PlaybookStats = {
    total: entries.length,
    awaiting: 0,
    approved: 0,
    rejected: 0,
    closed: 0,
    aiAuthored: 0,
    approvalRate: null,
    byPillar: { Investigate: 0, Correlate: 0, Prevent: 0, Recover: 0 },
  };
  for (const entry of entries) {
    stats[stateGroup(entry.state)]++;
    stats.byPillar[entry.pillarKind]++;
    if (entry.proposerKind === 'ReasoningAgent') stats.aiAuthored++;
  }
  const decided = stats.approved + stats.rejected;
  stats.approvalRate = decided > 0 ? stats.approved / decided : null;
  return stats;
}

/**
 * What approving this specific proposal does — and, as importantly, doesn't. Approval in the
 * Playbook Ledger is always "a human agrees this is sound"; it never executes a recovery.
 */
export function approvalMeaning(entry: Pick<PlaybookEntry, 'pillarKind' | 'proposalKind'>): string {
  if (entry.proposalKind === 'PreventionRuleProposal') {
    return 'Approving makes this an active, observe-only prevention rule. It records when the pattern recurs — it never replays, purges or blocks anything.';
  }
  if (entry.proposalKind === 'ReplayPlan' || entry.pillarKind === 'Recover') {
    return 'Approving records that a human agrees this replay is sound. It does not replay anything — to replay, approve the message in the Approval Queue, where the Eligibility Gate checks it again.';
  }
  return 'Approving records that a human agrees this finding is sound. It never triggers a replay or purge, and never changes autonomy.';
}
