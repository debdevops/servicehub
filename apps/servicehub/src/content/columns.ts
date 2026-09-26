/**
 * What every column means, in one place.
 *
 * Every table header carries a small (i) that opens the entry written here, so a person is never left guessing what a column is
 * or where its number comes from. Written once for the same reason `explanations.ts` is: a definition worded two ways is a
 * definition that disagrees with itself. Plain words, cloud-neutral — where the clouds differ, the entry says how.
 */
export interface ColumnHelp {
  readonly title: string
  readonly text: string
}

const h = (title: string, text: string): ColumnHelp => ({ title, text })

export const columnHelp = {
  deadLetters: {
    when: h('When', 'When ServiceHub first saw this message in the dead-letter queue. No cloud reports the exact moment it was set aside, so this is the honest stand-in — never the time it was first sent.'),
    where: h('Queue or topic', 'Where the message is stuck. A plain queue shows its name. A message stuck under a topic shows the topic and the subscription that could not deliver it, because a topic itself holds nothing — its subscriptions do. The small tag says which one it is.'),
    failedBecause: h('Failed because', 'The coloured tag is the reason the cloud or your application recorded — a fact. Under it is the error text that came with it, then ServiceHub’s plain-English reading (marked with a lightbulb — a suggestion, not something the cloud reported). If the cloud gave no error text, it says so.'),
    tries: h('Tries', 'How many times a consumer picked this message up before it was set aside. Many tries on a message that fails at once usually means the message itself is wrong; one or two usually means something else was down. A dash means this cloud does not report it.'),
    waiting: h('Waiting', 'How long it has been sitting in the dead-letter queue since ServiceHub first saw it. A long wait is not a problem by itself — nothing is lost — but old messages are worth a look first.'),
    now: h('Now', 'Whether it is still in the dead-letter queue. If it left, when ServiceHub noticed and how, as far as anything recorded it. “Did not see how” means it is gone — drained by another tool, expired, or consumed — not that anyone fixed it.'),
    size: h('Size', 'The size of the message body. Very large messages are a common cause of timeouts and rejections.'),
    details: h('Details', 'Opens this message beside the table: why it failed, its body, its properties and its history. From there you can replay it. Nothing is changed by opening it.'),
  },
  active: {
    enqueued: h('Enqueued', 'When the message was put on the queue. It is waiting to be picked up; it has not failed.'),
    where: h('Queue or topic', 'The queue, or the topic subscription, this message is waiting in.'),
    delivery: h('Delivery', 'How many times a consumer has picked this message up so far. A climbing number is an early sign it may end up dead-lettered.'),
    age: h('Age', 'How long the message has been waiting. A queue whose oldest message keeps getting older is not being drained.'),
    size: h('Size', 'The size of the message body.'),
    view: h('View', 'Opens the message to read it. Looking does not touch the message. Active messages cannot be replayed — they have not failed.'),
    waitingNow: h('Waiting now', 'How many messages are waiting in this queue right now, as counted by the cloud. “Can’t count here” means this cloud does not report it — it is not zero.'),
    deadLettered: h('Dead-lettered', 'How many messages this queue has set aside in its dead-letter queue right now, as counted by the cloud. “Can’t count here” means the cloud does not report it.'),
  },
  replayed: {
    replayed: h('Replayed', 'When the message was sent back to be processed again.'),
    from: h('From', 'The queue or subscription the message was put back onto.'),
    by: h('By', 'Who asked for the replay: a person, a rule, or the Agent. Where nobody signed in, it says “from this browser session” rather than inventing a name.'),
    messages: h('Messages', 'How many messages this replay covered.'),
    result: h('Result', 'How it ended. “Verified — stayed fixed” only appears where the cloud can prove the queue stayed empty. “Verification required” means it was sent but the cloud cannot prove it held. “Came back” means it failed again.'),
    details: h('Details', 'Opens the message this replay was for.'),
  },
  ledger: {
    time: h('Time', 'When this recovery action began.'),
    entity: h('Entity', 'The queue or subscription the recovery acted on.'),
    cloud: h('Cloud', 'Which cloud the action ran in.'),
    namespace: h('Namespace', 'The namespace the action ran in, with its environment. Shown as recorded when it cannot be matched to exactly one connected namespace.'),
    by: h('By', 'Who or what started it: a person, a rule or an agent.'),
    what: h('What', 'The kind of action and how many messages it covered.'),
    outcome: h('Outcome', 'How it ended. Recovered means it did not come back; Unverified means it was sent but cannot be proven; Returned means it failed again. These are never counted together.'),
    match: h('Match', 'How ServiceHub knew a returned message was the one it replayed: Exact means it carried the replay’s marker; otherwise it was matched by content and marked as less certain.'),
    open: h('Open', 'Opens the full evidence trail for this entry.'),
  },
  fleet: {
    namespace: h('Namespace', 'One connected cloud account — an Azure namespace, an AWS account and region, or a Google project.'),
    env: h('Env', 'What kind of environment it is (Dev, UAT or Prod). ServiceHub’s automatic features never run in Prod.'),
    deadLettered: h('Dead-lettered', 'How many messages are in this namespace’s dead-letter queues right now, counted by the cloud itself.'),
    new: h('New', 'Messages that landed in a dead-letter queue during the chosen window. Shown only where ServiceHub looks on its own; elsewhere it says “—”, not zero.'),
    resolved: h('Resolved', 'Messages that left a dead-letter queue during the window — replayed or cleared.'),
    topFailure: h('Top failure', 'The failure reason carried by the most messages still waiting in this namespace.'),
    health: h('Health', 'Needs a look: something came back after a replay, or the connection failed. Healthy: nothing needs attention. Can’t tell: ServiceHub does not look at this cloud on its own, so it will not claim it is fine.'),
  },
  signatures: {
    signature: h('Signature', 'A fingerprint for one way of failing — same queue, same kind of error — so many messages become one thing to reason about.'),
    messages: h('Messages', 'How many messages ServiceHub has recorded with this signature, including ones already handled.'),
    days: h('Days', 'Messages per day over the chosen window, oldest on the left. A rising bar means it is getting worse.'),
    replays: h('Replays', 'From replays already done: how many stayed fixed, out of those that could be verified. “Not replayed” means there is no evidence either way.'),
  },
  drawer: {
    messageId: h('Message ID', 'The cloud’s own identifier for this message. Copy it to find the same message in your cloud’s console or logs.'),
    where: h('Queue or topic', 'Where the message is stuck: a queue, or a topic’s subscription written as topic › subscription.'),
    enqueued: h('Enqueued', 'When the message was first put on the queue — before it failed. Compare with “set aside” to see how long it lived.'),
    tries: h('Tries', 'How many times a consumer picked the message up before it was set aside. A dash means this cloud does not report it.'),
  },
  tiles: {
    deadLetters: h('Dead letters', 'How many messages are stuck in this cloud’s dead-letter queues right now, counted by the cloud itself. “Can’t count here” means this cloud does not report it — that is not zero.'),
    active: h('Active messages', 'How many messages are waiting to be processed right now across this cloud’s queues and topic subscriptions. They have not failed.'),
  },
  glance: {
    namespaces: h('Namespaces', 'How many accounts of this cloud you have connected to ServiceHub.'),
    queues: h('Queues', 'Queues ServiceHub can see. “With dead letters” counts those holding at least one; “Healthy” means none do. Where the cloud cannot count per queue, no verdict is shown.'),
    topics: h('Topics', 'Topics ServiceHub can see. A topic itself holds no messages — each of its subscriptions has its own queue and its own dead-letter queue.'),
    subscriptions: h('Subscriptions', 'Subscriptions under your topics. Each one is a queue of its own, so each can hold dead letters.'),
  },
  attention: {
    queue: h('Queue or topic', 'A queue, or a topic subscription, that is holding dead letters. Counted by the cloud, so it works even where ServiceHub cannot browse messages.'),
    dead: h('Dead', 'How many dead letters it holds right now.'),
  },
} as const
