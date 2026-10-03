import {
  describeApprovalQueueReason,
  describeRecoveryDetailReason,
  type RecoveryEntryState,
  type RecoveryLedgerEntry,
  type RecoveryOperation,
  type RecoveryOperationDetail,
} from '@servicehub/ui-shared/lib/api/recovery';
import type { Tone } from './ui';

/**
 * Pure derivations over Recovery Evidence Ledger rows — how an operation turned out, the headline
 * numbers, and the Detect → Diagnose → Propose → Approve → Execute → Verify story. Nothing here
 * invents a fact: every value is counted from an entry's recorded state or an event's recorded
 * type, and anything the ledger can't answer is reported as unknown rather than guessed.
 */

export type EntryBucket = 'recovered' | 'purged' | 'failed' | 'inProgress' | 'unresolved' | 'blocked' | 'writtenOff';

export function bucketEntryState(state: RecoveryEntryState): EntryBucket {
  switch (state) {
    case 'Recovered':
      return 'recovered';
    case 'Discarded':
      return 'purged';
    case 'ExecutionFailed':
    case 'Returned':
      return 'failed';
    case 'Executing':
    case 'Observing':
      return 'inProgress';
    case 'Declined':
      return 'blocked';
    case 'WrittenOff':
      return 'writtenOff';
    // ExecutionUnknown, Unverified, Expired — the ledger could not confirm the outcome.
    default:
      return 'unresolved';
  }
}

export type BucketCounts = Record<EntryBucket, number>;

export function countBuckets(entries: readonly RecoveryLedgerEntry[]): BucketCounts {
  const counts: BucketCounts = { recovered: 0, purged: 0, failed: 0, inProgress: 0, unresolved: 0, blocked: 0, writtenOff: 0 };
  for (const entry of entries) counts[bucketEntryState(entry.state)]++;
  return counts;
}

export type OperationOutcome =
  | 'Recovered'
  | 'Purged'
  | 'Failed'
  | 'Partial'
  | 'InProgress'
  | 'NeedsReview'
  | 'Blocked'
  | 'Unknown';

export const OUTCOME_META: Record<OperationOutcome, { label: string; tone: Tone; description: string }> = {
  Recovered: {
    label: 'Recovered',
    tone: 'green',
    description: 'Every replayed message stayed out of the dead-letter queue for its full observation window.',
  },
  Purged: { label: 'Purged', tone: 'gray', description: 'Deliberately deleted, as requested — nothing to observe afterwards.' },
  Failed: { label: 'Failed', tone: 'red', description: 'The provider rejected the call, or the messages returned to the dead-letter queue.' },
  Partial: { label: 'Partial', tone: 'amber', description: 'Some messages recovered and some did not — open it to see which.' },
  InProgress: { label: 'In progress', tone: 'blue', description: 'ServiceHub is still waiting on the provider or watching for a recurrence.' },
  NeedsReview: {
    label: 'Unverified',
    tone: 'amber',
    description: 'ServiceHub could not confirm the outcome (for example, the provider cannot prove a message did not return).',
  },
  Blocked: { label: 'Blocked by gate', tone: 'gray', description: 'The Eligibility Gate refused before any provider call was made.' },
  Unknown: {
    label: 'See details',
    tone: 'gray',
    description: "Only part of this operation's entries are loaded on this page — open it for the complete outcome.",
  },
};

export interface OperationSummary {
  outcome: OperationOutcome;
  counts: BucketCounts;
  /** Entries loaded for this operation on this page. */
  loaded: number;
  /** Whether every entry the operation recorded is loaded, so the outcome is complete. */
  complete: boolean;
}

export function summarizeOperation(
  operation: Pick<RecoveryOperation, 'kind' | 'entryCount'>,
  entries: readonly RecoveryLedgerEntry[],
): OperationSummary {
  const counts = countBuckets(entries);
  const loaded = entries.length;
  const complete = loaded > 0 && loaded >= operation.entryCount;
  const succeeded = counts.recovered + counts.purged;

  let outcome: OperationOutcome;
  if (loaded === 0) outcome = 'Unknown';
  else if (counts.inProgress > 0) outcome = 'InProgress';
  else if (!complete) outcome = 'Unknown';
  else if (counts.blocked === loaded) outcome = 'Blocked';
  else if (succeeded === loaded) outcome = operation.kind === 'Purge' ? 'Purged' : 'Recovered';
  else if (succeeded === 0 && counts.failed > 0) outcome = 'Failed';
  else if (succeeded > 0) outcome = 'Partial';
  else outcome = 'NeedsReview';

  return { outcome, counts, loaded, complete };
}

export function groupEntriesByOperation(entries: readonly RecoveryLedgerEntry[]): Map<string, RecoveryLedgerEntry[]> {
  const map = new Map<string, RecoveryLedgerEntry[]>();
  for (const entry of entries) {
    const list = map.get(entry.operationId);
    if (list) list.push(entry);
    else map.set(entry.operationId, [entry]);
  }
  return map;
}

export interface LedgerStats {
  entries: number;
  counts: BucketCounts;
  /**
   * Recovered ÷ (recovered + failed) — only replays with a verified verdict count. Unverified,
   * blocked, written-off and purged entries are excluded rather than silently treated as failures
   * or successes. `null` when nothing has a verdict yet.
   */
  verifiedRecoveryRate: number | null;
}

export function computeLedgerStats(entries: readonly RecoveryLedgerEntry[]): LedgerStats {
  const counts = countBuckets(entries);
  const decided = counts.recovered + counts.failed;
  return {
    entries: entries.length,
    counts,
    verifiedRecoveryRate: decided > 0 ? counts.recovered / decided : null,
  };
}

// `__spa__` is the identity every request from the ServiceHub web UI authenticates as (the SPA
// token), i.e. "a person clicking in this app" — shown as such rather than as a raw token name.
const SPA_IDENTITY = '__spa__';

/** Display name for a recorded actor identity. */
export function displayActor(identity: string): string {
  return identity === SPA_IDENTITY ? 'ServiceHub UI user' : identity;
}

/** Humanizes the actor behind an operation — the "who acted" column. */
export function describeActor(operation: Pick<RecoveryOperation, 'actorKind' | 'actorIdentity' | 'trigger'>): {
  label: string;
  isHuman: boolean;
} {
  const isHuman = operation.actorKind === 'User' || operation.actorKind === 'ApiKey';
  return { label: displayActor(operation.actorIdentity), isHuman };
}

export const TRIGGER_LABELS: Record<RecoveryOperation['trigger'], string> = {
  Manual: 'Manual',
  BulkJob: 'Bulk job',
  SignatureJob: 'Signature replay',
  RuleReplayAll: 'Rule: replay all',
  AutoRule: 'Auto-Replay Rule',
  StartupRecovery: 'Startup recovery',
};

// ─── Lifecycle story ────────────────────────────────────────────────────────

export type StepStatus = 'done' | 'failed' | 'pending' | 'warn' | 'blocked' | 'skipped';

export interface LifecycleStep {
  key: 'detect' | 'diagnose' | 'propose' | 'approve' | 'execute' | 'verify';
  label: string;
  status: StepStatus;
  title: string;
  detail?: string;
  at?: string;
}

function readReasonCode(detailJson: string | null): string | null {
  if (!detailJson) return null;
  try {
    const parsed = JSON.parse(detailJson) as { reasonCode?: unknown };
    return typeof parsed.reasonCode === 'string' ? parsed.reasonCode : null;
  } catch {
    return null;
  }
}

function providerName(provider: string | null): string {
  switch (provider?.toLowerCase()) {
    case 'azure':
      return 'Azure Service Bus';
    case 'aws':
      return 'AWS SQS';
    case 'gcp':
      return 'GCP Pub/Sub';
    default:
      return 'the provider';
  }
}

function plural(count: number, noun: string): string {
  return `${count} ${noun}${count === 1 ? '' : 's'}`;
}

/**
 * Builds the six-step recovery story from one operation's full detail. Steps the ledger has no
 * record for are marked `skipped` with an explicit "nothing recorded" line, never back-filled.
 */
export function buildRecoveryLifecycle(detail: RecoveryOperationDetail): LifecycleStep[] {
  const { operation, entries, events } = detail;
  const counts = countBuckets(entries);
  const first = entries[0];
  const verb = operation.kind === 'Purge' ? 'purge' : 'replay';

  const detectReason = entries.find(e => e.deadLetterReasonSnapshot)?.deadLetterReasonSnapshot ?? null;
  const entity = first?.entityNameSnapshot ?? first?.targetEntity ?? null;
  const detect: LifecycleStep = entity
    ? {
        key: 'detect',
        label: 'Detect',
        status: 'done',
        title: `Dead-lettered on ${entity}`,
        detail: detectReason ? `Recorded reason: ${detectReason}` : 'No dead-letter reason was recorded for these messages.',
      }
    : { key: 'detect', label: 'Detect', status: 'skipped', title: 'No source message recorded' };

  const category = entries.find(e => e.failureCategorySnapshot)?.failureCategorySnapshot ?? null;
  const signature = entries.find(e => e.signatureHashSnapshot)?.signatureHashSnapshot ?? null;
  const diagnose: LifecycleStep =
    category || signature
      ? {
          key: 'diagnose',
          label: 'Diagnose',
          status: 'done',
          title: category ? `Classified as ${category}` : 'Failure signature computed',
          detail: signature ? `Failure signature ${signature.slice(0, 12)}…` : 'No failure signature was computed for this operation.',
        }
      : { key: 'diagnose', label: 'Diagnose', status: 'skipped', title: 'No classification recorded' };

  const proposeTitles: Record<RecoveryOperation['trigger'], [string, string]> = {
    Manual: ['Started by an operator', 'No automated proposal — a person chose to act.'],
    BulkJob: ['Bulk operation', 'An operator selected these messages for a bulk action.'],
    SignatureJob: ['Signature replay', 'An operator chose to replay one failure signature.'],
    RuleReplayAll: [
      operation.sourceRuleId != null ? `Rule #${operation.sourceRuleId}: replay all` : 'Rule: replay all',
      'An operator ran "replay all" for an Auto-Replay Rule.',
    ],
    AutoRule: [
      operation.sourceRuleId != null ? `Matched Auto-Replay Rule #${operation.sourceRuleId}` : 'Matched an Auto-Replay Rule',
      'A rule you configured matched these messages.',
    ],
    StartupRecovery: ['Startup recovery', 'ServiceHub resumed an operation interrupted by a restart.'],
  };
  const [proposeTitle, proposeDetail] = proposeTitles[operation.trigger];
  const propose: LifecycleStep = { key: 'propose', label: 'Propose', status: 'done', title: proposeTitle, detail: proposeDetail, at: operation.openedAt };

  let approve: LifecycleStep;
  if (entries.length > 0 && counts.blocked === entries.length) {
    const declinedEvent = events.find(e => e.entryId && readReasonCode(e.detailJson));
    const reason = describeApprovalQueueReason(readReasonCode(declinedEvent?.detailJson ?? null));
    approve = {
      key: 'approve',
      label: 'Approve',
      status: 'blocked',
      title: 'Refused by the Eligibility Gate',
      detail: reason ?? 'The gate refused before any provider call. No reason code was recorded.',
    };
  } else if (operation.actorKind === 'User' || operation.actorKind === 'ApiKey') {
    approve = {
      key: 'approve',
      label: 'Approve',
      status: 'done',
      title: `Authorized by ${displayActor(operation.actorIdentity)}`,
      detail: operation.reason
        ? `Stated reason: “${operation.reason}”`
        : `${operation.actorKind === 'User' ? 'A person' : 'An API key'} started this ${verb}; the Eligibility Gate still checked it.`,
    };
  } else if (operation.actorKind === 'Automation') {
    approve = {
      key: 'approve',
      label: 'Approve',
      status: 'done',
      title: 'No human approval required',
      detail: 'This failure signature had earned unattended trust, and the Eligibility Gate allowed it.',
    };
  } else {
    approve = {
      key: 'approve',
      label: 'Approve',
      status: 'done',
      title: 'System-initiated',
      detail: `Started by ${operation.actorIdentity} (${TRIGGER_LABELS[operation.trigger]}).`,
    };
  }

  const attempted = entries.length - counts.blocked;
  const accepted = events.filter(e => e.eventType === 'ProviderAccepted').length;
  const rejected = events.filter(e => e.eventType === 'ProviderRejected').length;
  const unknown = events.filter(e => e.eventType === 'ExecutionUnknown').length;
  const firstBegun = events.find(e => e.eventType === 'EntryBegun')?.occurredAt;
  const execute: LifecycleStep =
    attempted === 0
      ? { key: 'execute', label: 'Execute', status: 'skipped', title: 'Nothing was sent to the provider' }
      : {
          key: 'execute',
          label: 'Execute',
          status: rejected > 0 && accepted === 0 ? 'failed' : unknown > 0 || rejected > 0 ? 'warn' : 'done',
          title: `Asked ${providerName(operation.providerSnapshot)} to ${verb} ${plural(attempted, 'message')}`,
          detail: [`${accepted} accepted`, rejected > 0 ? `${rejected} rejected` : null, unknown > 0 ? `${unknown} outcome unknown` : null]
            .filter(Boolean)
            .join(' · '),
          at: firstBegun,
        };

  let verify: LifecycleStep;
  if (operation.kind === 'Purge') {
    verify = { key: 'verify', label: 'Verify', status: 'skipped', title: 'Nothing to observe', detail: 'A purge deletes the message by design.' };
  } else if (attempted === 0) {
    verify = { key: 'verify', label: 'Verify', status: 'skipped', title: 'Nothing to verify' };
  } else {
    const returned = entries.filter(e => e.state === 'Returned').length;
    const unverified = entries.filter(e => e.state === 'Unverified' || e.state === 'ExecutionUnknown' || e.state === 'Expired').length;
    const unverifiedReason = events.map(e => (e.entryId ? describeRecoveryDetailReason(e.detailJson) : null)).find(Boolean) ?? null;
    const parts = [
      `${counts.recovered} did not return`,
      returned > 0 ? `${returned} returned to the DLQ` : null,
      unverified > 0 ? `${unverified} could not be verified` : null,
      counts.inProgress > 0 ? `${counts.inProgress} still being watched` : null,
    ].filter(Boolean);
    const status: StepStatus =
      counts.inProgress > 0 ? 'pending' : returned > 0 || counts.failed > 0 ? 'failed' : unverified > 0 ? 'warn' : 'done';
    const windowEnds = entries.map(e => e.observationWindowEndsAt).filter((v): v is string => !!v).sort();
    const windowEnd = windowEnds[windowEnds.length - 1];
    verify = {
      key: 'verify',
      label: 'Verify',
      status,
      title: status === 'pending' ? 'Watching the dead-letter queue' : 'Observation window closed',
      detail: [parts.join(' · '), unverified > 0 && unverifiedReason ? unverifiedReason : null].filter(Boolean).join(' — '),
      at: status === 'pending' ? windowEnd : undefined,
    };
  }

  return [detect, diagnose, propose, approve, execute, verify];
}
