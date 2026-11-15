/**
 * The eval set: real ways people ask, and the entry that must answer. Add a case for every new entry and for every miss
 * a user reports. `test/eval.test.ts` requires every case to be the top answer.
 */
export const evalCases: readonly { readonly q: string; readonly expect: string }[] = [
  // Replay (live)
  { q: 'how do I replay dlq', expect: 'dlq-replay' },
  { q: 'How to replay dead letter messages?', expect: 'dlq-replay' },
  { q: 'resubmit messages from dead letter queue', expect: 'dlq-replay' },
  { q: 'retry failed dlq messages', expect: 'dlq-replay' },
  { q: 'redrive dlq', expect: 'dlq-replay' },
  { q: 'can you replay them for me', expect: 'dlq-replay' },
  { q: 'replay many messages at once', expect: 'help:replay-many' },
  { q: 'bulk replay', expect: 'help:replay-many' },
  { q: "why can't I replay", expect: 'help:no-replay' },
  // Status (live)
  { q: 'how many dead letters do I have', expect: 'dlq-status' },
  { q: "what's in my dlq", expect: 'dlq-status' },
  { q: 'why are my messages dead lettering', expect: 'dlq-status' },
  { q: 'which queue has the most dead letters', expect: 'dlq-status' },
  { q: 'why aws dead letters not listed', expect: 'dlq-status' },
  { q: 'why did my message fail', expect: 'help:why-failed' },
  // Replay results (live)
  { q: 'did my replay work', expect: 'replay-results' },
  { q: 'did the fix hold', expect: 'replay-results' },
  { q: 'replay status', expect: 'replay-results' },
  { q: 'verification required', expect: 'help:did-it-work' },
  // Connections (live) and connecting
  { q: 'which clouds are connected', expect: 'connections' },
  { q: 'is my namespace connected', expect: 'connections' },
  { q: 'connect aws', expect: 'connect-cloud' },
  { q: 'connect azure service bus', expect: 'connect-cloud' },
  { q: 'connect google cloud pubsub', expect: 'connect-cloud' },
  { q: 'what permissions does servicehub need', expect: 'connect-cloud' },
  // Reasons
  { q: 'what does MaxDeliveryCountExceeded mean', expect: 'reason-max-delivery' },
  { q: 'TTLExpiredException', expect: 'reason-ttl' },
  // Purge
  { q: 'purge a dead letter', expect: 'dlq-purge' },
  { q: 'delete messages from dlq', expect: 'dlq-purge' },
  { q: 'can you delete these for me', expect: 'dlq-purge' },
  { q: 'why is purge not offered', expect: 'dlq-purge' },
  // Agent and rules
  { q: 'auto replay rule', expect: 'help:auto-rules' },
  { q: 'emergency stop', expect: 'help:stop-all' },
  { q: 'handed to a person', expect: 'help:handed-off' },
  // Running and hosting
  { q: 'run with docker', expect: 'run-docker' },
  { q: 'run locally without docker', expect: 'run-local' },
  { q: 'how do I start servicehub', expect: 'run-local' },
  { q: 'host on azure app service', expect: 'doc:docs/HOSTING-AZURE-APP-SERVICE.md' },
  { q: 'rotate encryption key', expect: 'doc:docs/ENCRYPTION-KEY-ROTATION.md' },
  { q: 'backup and restore', expect: 'doc:docs/BACKUP-RESTORE.md' },
  { q: 'recovery evidence ledger', expect: 'doc:docs/RECOVERY-EVIDENCE.md' },
  { q: 'logic app failed runs', expect: 'doc:docs/RECOVERY-EVIDENCE.md#8-cloud-resubmit-of-a-failed-run-azure-logic-apps' },
]

/** Questions the bot must not pretend to answer. */
export const outOfScope: readonly string[] = ['weather in paris', 'write me a poem', 'what is the capital of france']
