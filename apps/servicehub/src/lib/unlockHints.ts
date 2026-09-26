/**
 * Unlock hints (unit 6.2): for every reason the eligibility gate can give, what it would take — and where to go. The code is the
 * fact the server recorded; this is its path. None is a dead end: each says what changes the answer, even when the answer is
 * "a person decides" or "wait".
 */
export interface UnlockHint {
  /** What holds it back, in plain words. */
  readonly why: string
  /** What would change the answer. */
  readonly takes: string
  /** Where to go to do that, when there is somewhere. */
  readonly go?: { readonly label: string; readonly href: string }
  /** Whether this could become automatic once earned — drawn in the "I could handle this myself" voice. */
  readonly earnable?: boolean
}

/** Every code `RecoveryEligibilityGate` (and the replay service) can return. A guard test keeps this list complete. */
export const GATE_REASON_CODES = [
  'EMERGENCY_STOP_ACTIVE', 'EMERGENCY_STOP_QUERY_ERROR', 'PURGE_AUTOMATION_PROHIBITED', 'PRODUCTION_ELEVATION_REQUIRED',
  'RECURRENCE_CAP_AMBIGUOUS_COLLISION', 'RECURRENCE_CAP_EXCEEDED', 'RECURRENCE_CAP_EXCEEDED_HEURISTIC', 'RECURRENCE_CAP_QUERY_ERROR',
  'AUTONOMY_SIGNATURE_HASH_MISSING', 'AUTONOMY_GRANT_QUERY_ERROR', 'AUTONOMY_GRANT_INSUFFICIENT', 'PROVIDER_CANNOT_VERIFY_ABSENCE',
  'RATE_LIMITED', 'FLEET_RATE_LIMITED', 'NOT_ACTIVE',
] as const

const couldNotRead = (what: string): UnlockHint => ({
  why: `ServiceHub couldn’t read its ${what}, so it held back rather than guess.`,
  takes: 'Try again in a moment. If it keeps happening, the Agents page shows which part of ServiceHub is not reporting.',
  go: { label: 'Agents', href: '/advanced/agents' },
})

const recurrence: UnlockHint = {
  why: 'It has been replayed and came back too many times — replaying again is unlikely to help.',
  takes: 'Fix what makes it fail, then replay it. Failure Signatures shows what it keeps failing on and whether replays ever held.',
  go: { label: 'See its signature', href: '/advanced/signatures' },
}

export const unlockHints: Readonly<Record<(typeof GATE_REASON_CODES)[number], UnlockHint>> = {
  EMERGENCY_STOP_ACTIVE: { why: 'Emergency stop is on: nothing acts on its own.', takes: 'An Admin switches it off in Settings when it is safe again.', go: { label: 'Settings', href: '?modal=settings' } },
  EMERGENCY_STOP_QUERY_ERROR: couldNotRead('emergency-stop state'),
  PURGE_AUTOMATION_PROHIBITED: { why: 'Only a person may purge — never a rule or an agent.', takes: 'Open the message and purge it yourself, with a reason.', go: { label: 'Dead letters', href: '/?tab=dlq' } },
  PRODUCTION_ELEVATION_REQUIRED: { why: 'This namespace is marked Production, and ServiceHub 4.1.0 stays out of production.', takes: 'Replay it with your production tooling — or, if it is not production, change its environment in Connections.', go: { label: 'Connections', href: '?panel=connections' } },
  RECURRENCE_CAP_AMBIGUOUS_COLLISION: { why: 'Two different messages look identical, so ServiceHub can’t count this one’s past attempts safely.', takes: 'A person can look at it and decide.', go: { label: 'Waiting for a person', href: '/advanced/ledger?state=Waiting' } },
  RECURRENCE_CAP_EXCEEDED: recurrence,
  RECURRENCE_CAP_EXCEEDED_HEURISTIC: recurrence,
  RECURRENCE_CAP_QUERY_ERROR: couldNotRead('replay history'),
  AUTONOMY_SIGNATURE_HASH_MISSING: { why: 'It was recorded before ServiceHub grouped failures, so it has no track record to earn from.', takes: 'A person decides this one. New failures of the same kind will build a record.', go: { label: 'Waiting for a person', href: '/advanced/ledger?state=Waiting' } },
  AUTONOMY_GRANT_QUERY_ERROR: couldNotRead('record of what this failure has earned'),
  AUTONOMY_GRANT_INSUFFICIENT: { why: 'This kind of failure hasn’t earned automatic replay yet.', takes: 'Every replay of it that is verified as fixed counts. After 10 at 95% or better, rules may replay it on their own.', go: { label: 'Its track record', href: '/advanced/signatures' }, earnable: true },
  PROVIDER_CANNOT_VERIFY_ABSENCE: { why: 'This cloud can’t prove a replayed message stayed fixed.', takes: 'A DLQ observer on this cloud would let ServiceHub prove the queue really drained. Until then a person decides each time — that is the safe answer, not a fault.', go: { label: 'How to set it up', href: '?panel=help' }, earnable: true },
  RATE_LIMITED: { why: 'Many replays just happened here, so ServiceHub is pacing itself.', takes: 'Wait a few minutes and try again — nothing is lost while it waits.' },
  FLEET_RATE_LIMITED: { why: 'Many replays just happened across every cloud, so ServiceHub is pacing itself.', takes: 'Wait a few minutes and try again — nothing is lost while it waits.' },
  NOT_ACTIVE: { why: 'ServiceHub has already seen this message leave the dead-letter queue.', takes: 'Nothing to do here. See what became of it under Replayed, or “No longer stuck”.', go: { label: 'Replayed', href: '/?tab=replayed' } },
}

export function hintFor(code: string | null | undefined): UnlockHint {
  return (code && (unlockHints as Record<string, UnlockHint>)[code]) || {
    why: `A safety check held it back (${code ?? 'no reason given'}).`,
    takes: 'A person with approval rights can decide.',
    go: { label: 'Waiting for a person', href: '/advanced/ledger?state=Waiting' },
  }
}
