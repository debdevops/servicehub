/** A stable action name (`Namespace.Connect`) in plain words — shared by every activity list, so an
 *  action reads the same whether it's one cloud's Recent Activity or the All-clouds one. */
export const auditActionWords: Readonly<Record<string, string>> = {
  'Namespace.Connect': 'Connected a namespace',
  'Namespace.Remove': 'Removed a namespace',
  'Replay.Message': 'Replayed a message',
  'DeadLetters.Look': 'Looked at dead letters',
  'Replay.Bulk': 'Bulk replay',
  'Message.Purge': 'Purged a message',
  'Purge.Message': 'Purged a message',
  'Message.Send': 'Sent a message',
  'Backup.Create': 'Took a backup',
  'Backup.Restore': 'Staged a backup restore',
  'Governance.Grant': 'Granted a role',
  'Governance.Revoke': 'Revoked a role',
  'Agent.Pause': 'Paused an agent',
  'Agent.Resume': 'Resumed an agent',
  'Rule.Create': 'Made an Auto Replay rule',
  'Rule.Update': 'Changed an Auto Replay rule',
  'Rule.Toggle': 'Switched an Auto Replay rule',
  'Rule.Delete': 'Deleted an Auto Replay rule',
  'Rule.Generate': 'Generated Auto Replay rules',
}
