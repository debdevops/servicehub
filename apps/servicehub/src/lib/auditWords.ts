/** A stable action name (`Namespace.Connect`) in plain words — shared by every activity list, so an
 *  action reads the same whether it's one cloud's Recent Activity or the All-clouds one. */
export const auditActionWords: Readonly<Record<string, string>> = {
  'Namespace.Connect': 'Connected a namespace',
  'Namespace.Remove': 'Removed a namespace',
  'Replay.Message': 'Replayed a message',
  'DeadLetters.Look': 'Looked at dead letters',
  'Replay.Bulk': 'Bulk replay',
  'Message.Purge': 'Purged a message',
}
