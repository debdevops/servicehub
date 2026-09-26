/**
 * Intent headers.
 *
 * A dangerous action carries an explicit header saying what it intends. It means a replay cannot be
 * triggered by a stray prefetch, a link crawler or a URL copied out of DevTools without the header
 * that came with it.
 *
 * It is not authentication. It closes the common accident, not a determined attacker.
 */
export const INTENT_HEADER = 'X-ServiceHub-Intent'

/** The intents ServiceHub recognises. Add one when a new dangerous action arrives. */
export const Intent = {
  ReplayMessage: 'replay-message',
  BulkReplay: 'bulk-replay',
  DeleteMessage: 'delete-message',
  CreateNamespace: 'create-namespace',
  DeleteNamespace: 'delete-namespace',
  LookAtDeadLetters: 'look-at-dead-letters',
  PauseAgent: 'pause-agent',
  ResumeAgent: 'resume-agent',
  ApproveEscalation: 'approve-escalation',
  DeclineEscalation: 'decline-escalation',
  AddChannel: 'add-channel',
  RemoveChannel: 'remove-channel',
  EmergencyStop: 'emergency-stop',
  SendMessage: 'send-message',
  PurgeMessage: 'purge-message',
  CreateBackup: 'create-backup',
  RestoreBackup: 'restore-backup',
  GrantRole: 'grant-role',
  RevokeRole: 'revoke-role',
} as const

export type IntentValue = (typeof Intent)[keyof typeof Intent]

/** Request headers declaring an intent. */
export function withIntent(intent: IntentValue): Record<string, string> {
  return { [INTENT_HEADER]: intent }
}
