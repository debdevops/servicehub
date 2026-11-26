import type { EntryState, LedgerEvent } from './api/recovery'

/**
 * The ledger's words. States use the enum's own vocabulary and never say more than is proven:
 * `Recovered` is "did not return", NEVER "succeeded" (V2), and `Unverified` is not a failure — it says the
 * confirmation is unproven (V1, C3).
 */
export const stateChip: Readonly<Record<EntryState, string>> = {
  Executing: 'In flight',
  Observing: 'Watching',
  ExecutionFailed: 'Execution failed',
  ExecutionUnknown: 'Outcome unknown',
  Recovered: 'Recovered',
  Returned: 'Returned',
  Discarded: 'Discarded',
  Unverified: 'Unverified',
  WrittenOff: 'Written off',
  Expired: 'Expired',
  Declined: 'Declined',
}

export const stateMeaning: Readonly<Record<EntryState, string>> = {
  Executing: 'the cloud call is in flight',
  Observing: 'accepted — ServiceHub is watching for it to come back',
  ExecutionFailed: 'nothing was sent back — the cloud refused it, or the message could not be found in the dead-letter queue',
  ExecutionUnknown: 'contact was lost, or ServiceHub stopped, before the answer was recorded — whether it was sent is not known; nothing tries again until a person looks',
  Recovered: 'did not return',
  Returned: 'the failure came back',
  Discarded: 'deliberately purged from the queue',
  Unverified: 'replayed, but the cloud can’t prove the queue stayed empty',
  WrittenOff: 'a person closed it with a written note — this is not a recovery',
  Expired: 'aged out without an outcome',
  Declined: 'blocked before any cloud was contacted',
}

/**
 * What a state means for a cloud redrive (kind `Redrive`). Queue-replay wording ("replayed", "the dead-letter queue", "the queue stayed
 * empty") would be false here: a redrive restarts a failed run inside the cloud, and acceptance says nothing about how the run then ends.
 */
const redriveMeaning: Readonly<Partial<Record<EntryState, string>>> = {
  Executing: 'the request to move the run on is in flight',
  ExecutionFailed: 'nothing was changed — the cloud refused the request, or this connection may not do it',
  ExecutionUnknown: 'contact was lost, or ServiceHub stopped, before the answer was recorded — whether the cloud took the request is not known; nothing tries again until a person looks',
  Unverified: 'the cloud accepted the request, but ServiceHub cannot confirm the run then finished fixed',
}

/** The meaning of a state for an entry of this kind. A kind with no words of its own gets the replay words. */
export const meaningOf = (state: EntryState, kind?: string): string =>
  (kind === 'Redrive' ? redriveMeaning[state] : undefined) ?? stateMeaning[state]

/** Tone for a state chip: green only for proof, red for a return or failure, amber for "unproven / unknown". */
export const stateTone = (s: EntryState): 'good' | 'bad' | 'warn' | 'neutral' =>
  s === 'Recovered' ? 'good' : s === 'Returned' || s === 'ExecutionFailed' ? 'bad' : s === 'Unverified' || s === 'ExecutionUnknown' ? 'warn' : 'neutral'

const reasons: Readonly<Record<string, string>> = {
  NAMESPACE_DEREGISTERED: 'the namespace was removed, so nothing was watching',
  NAMESPACE_UNKNOWN: 'the namespace is not known',
}

function detailOf(json: string | null): Record<string, unknown> {
  if (!json) return {}
  try {
    const parsed: unknown = JSON.parse(json)
    return parsed && typeof parsed === 'object' ? (parsed as Record<string, unknown>) : {}
  } catch {
    return {} // a free-text note is shown as none — the raw text is never trusted as structure
  }
}

/** One event as a person reads it: a headline and, when there is one, the fact behind it. */
export function describeEvent(e: LedgerEvent, cloud: string, kind?: string): { title: string; note: string | null } {
  const d = detailOf(e.detail)
  const redrive = kind === 'Redrive'
  switch (e.eventType) {
    case 'OperationOpened': return { title: 'Decision recorded', note: null }
    case 'EntryBegun': return { title: redrive ? 'Request to move the run on' : 'Replay begun', note: null }
    case 'ProviderAccepted': return { title: redrive ? `${cloud} accepted the request` : `Sent, and accepted by ${cloud}`, note: null }
    case 'ProviderRejected': {
      const code = typeof d.errorCode === 'string' ? d.errorCode : null
      // "Not found" is not a refusal: the cloud was never asked to take it, because the message was not in the dead-letter queue.
      return !redrive && /NotFound|NOT_FOUND/.test(code ?? '')
        ? { title: 'Not sent — the message was not found in the dead-letter queue', note: code }
        : { title: `${cloud} rejected it`, note: code }
    }
    case 'ExecutionUnknown': return { title: 'Outcome unknown', note: 'it was not recorded whether it was sent' }
    case 'ObservationWindowOpened':
      return { title: 'Watching', note: typeof d.appliedObservationWindowHours === 'number' ? `${d.appliedObservationWindowHours} hour window` : null }
    case 'RecurrenceObserved': return { title: 'Came back', note: typeof d.collisionCount === 'number' ? `matched by contents · ${d.collisionCount} possible` : 'matched by its recovery ID' }
    case 'NoRecurrenceObserved': return { title: 'Window closed — did not come back', note: 'coverage was proven' }
    case 'ObservationUnavailable': {
      const reason = typeof d.reason === 'string' ? d.reason : null
      return {
        title: 'Window closed — could not be proven',
        note: reason ? (reasons[reason] ?? (reason.endsWith('_NO_ABSENCE_PROOF') ? `${cloud} cannot prove the queue stayed empty` : reason)) : null,
      }
    }
    case 'DispositionSet': return { title: 'Written off', note: e.detail }
    case 'OperatorNote': return { title: 'Note added', note: e.detail }
    default: return { title: e.eventType, note: null }
  }
}

/** "9f3c…44d0" — enough to compare by eye; the full value is in the entry's title. */
export const shortHash = (hash: string) => (hash.length > 12 ? `${hash.slice(0, 4)}…${hash.slice(-4)}` : hash)
