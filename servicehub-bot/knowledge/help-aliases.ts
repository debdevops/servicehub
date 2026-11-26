/**
 * Extra ways people ask for ServiceHub's own Help answers (`apps/servicehub/src/next/content/help.ts`), keyed by that
 * answer's id. The Help text itself is never copied here, so it stays written once, in the product. The build fails when
 * an id no longer exists, so a renamed Help answer cannot silently lose its aliases.
 */
export const helpAliases: Readonly<Record<string, readonly string[]>> = {
  'why-failed': ['Why did my message fail?', 'Dead letter reason', 'See the message body'],
  'replay-one': ['Replay a single message'],
  'replay-many': ['Bulk replay', 'Replay all dead letters', 'Replay many messages at once'],
  'did-it-work': ['Verification required', 'What does Unverified mean?', 'What does Verified mean?'],
  'asked-less': ['Stop asking me to approve', 'Too many approvals'],
  'agent-earns': ['Autonomy', 'When does the agent act alone?'],
  'stop-all': ['Emergency stop', 'Stop the agent', 'Pause everything'],
  'handed-off': ['Handed to a person', 'Needs your attention'],
  'no-replay': ['Replay button missing', "Why can't I replay?", 'Replay greyed out', 'Needs an Operator to replay'],
  'auto-rules': ['Auto replay rule', 'Auto replay rules', 'Auto replay', 'Automatic replay rule', 'Replay automatically', 'Create a replay rule'],
}
