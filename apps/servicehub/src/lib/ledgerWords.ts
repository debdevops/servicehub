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
  ExecutionFailed: 'the cloud rejected the call, so nothing was sent back',
  ExecutionUnknown: 'contact was lost mid-call — whether it was sent is not known',
  Recovered: 'did not return',
  Returned: 'the failure came back',
  Discarded: 'deliberately purged from the queue',
  Unverified: 'replayed, but the cloud can’t prove the queue stayed empty',
  WrittenOff: 'a person declared it unrecoverable',
  Expired: 'aged out without an outcome',
  Declined: 'blocked before any cloud was contacted',
}

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
export function describeEvent(e: LedgerEvent, cloud: string): { title: string; note: string | null } {
  const d = detailOf(e.detail)
  switch (e.eventType) {
    case 'OperationOpened': return { title: 'Decision recorded', note: null }
    case 'EntryBegun': return { title: 'Replay begun', note: null }
    case 'ProviderAccepted': return { title: `Sent, and accepted by ${cloud}`, note: null }
    case 'ProviderRejected': return { title: `${cloud} rejected it`, note: typeof d.errorCode === 'string' ? d.errorCode : null }
    case 'ExecutionUnknown': return { title: 'Outcome unknown', note: 'contact was lost before it was known whether it was sent' }
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
