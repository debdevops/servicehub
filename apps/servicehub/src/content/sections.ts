import type { ColumnHelp } from './columns'

const h = (title: string, text: string): ColumnHelp => ({ title, text })

/**
 * What each titled section of the app is, behind its (i) — the same idea as `columnHelp` for table headers and `widgetHelp` for Home's
 * cards. Written cloud-neutral: where the clouds differ, the entry says how, so one sentence is true for Azure, AWS and Google alike.
 */
export const sectionHelp = {
  tabs: {
    dlq: h('Dead letters', 'Messages that failed too many times and were set aside. Newest first. Pick one to see why it failed and put it back.'),
    active: h('Active', 'Messages waiting on queues right now. They have not failed, so they can be read but not replayed.'),
    replayed: h('Replayed', 'Messages already sent back: who did it, when, and whether the fix held. Unverified means the cloud cannot prove it either way.'),
    overview: h('Overview', 'The control plane in one screen: what needs a person, how recoveries ended, what ServiceHub may do alone, and how each cloud can prove a fix.'),
    insights: h('Insights', 'Patterns found in the data — for example queues filling faster than they drain. Each says what it saw and how sure it is; none acts on its own.'),
    summary: h('Summary', 'Whether this failure is growing, whether replaying helps, and whether ServiceHub may replay it on its own.'),
    incident: h('Incident', 'The story of this failure over time: when it began, what was tried and what came of it.'),
  },
  replay: {
    message: h('The message', 'Exactly which message will be sent back: its own ID, where it is stuck, the reason the cloud recorded, and what it says. Nothing here changes it — replaying sends this same message again.'),
  },
  dlq: {
    total: h('Total messages', 'Dead letters ServiceHub has recorded in this view, and — as the line — how many first appeared each day over the last two weeks. The three counts beside it split the total by the reason the cloud recorded.'),
    refresh: h('Refresh', 'Reads the list again. Where ServiceHub looks on its own the list also refreshes itself; “Last updated” says when it was last read.'),
  },
  filters: {
    cloud: h('Cloud', 'Show only what happened in one cloud. Azure, AWS and Google are read the same way.'),
    namespace: h('Namespace', 'Show only one connected namespace, or every namespace of one environment. Choose a cloud first to shorten the list.'),
    entity: h('Queue or topic', 'Show only one queue, or one topic subscription. Names are exactly as recorded.'),
    by: h('By', 'Who made the replay. People: a person, or an API key a person issued. ServiceHub autonomous: ServiceHub’s own agents and rules acting on their own, only where a failure has earned it.'),
    search: h('Search', 'Finds a word in the queue, namespace, failure reason or who did it. It looks at what ServiceHub recorded — never inside message bodies.'),
  },
  advanced: {
    recovery: h('Recovery', 'Every replay in the chosen window, sorted by how it ended. Recovered (did not come back) and Unverified (sent, but the cloud cannot prove the queue stayed empty) are never merged. Pick an outcome to open it in the Ledger.'),
    needsAttention: h('Needs your attention', 'The things only a person can answer: replays the Agent stopped and asked about, rules that stopped themselves, and agents that stopped reporting. It is empty when nothing is waiting on you.'),
  },
  agents: {
    acting: h('Acting agents', 'The only agents that can change something in a cloud, and only within the safety checks and your permissions. Everything they do is written to the Recovery Ledger with who and why.'),
    watching: h('Watching agents', 'Agents that look, record and decide. They never change your cloud; the worst they can do is raise something for a person.'),
    health: h('Agent Health', 'How the running agents are doing, judged from their own rounds — never self-reported. Degraded: it ran but something it needed was partly unavailable. Unhealthy: its rounds keep failing. Not run yet: it has not finished a round since the server started.'),
    contract: h('What it may and may not do', 'The agent’s written contract. It is enforced, not just described: an agent that tries something outside it is stopped and flagged.'),
    activity: h('Recent activity', 'What this agent did since the server last started, newest first. A run of “nothing to do” is folded into one line so real events stand out. Older history is in the Ledger.'),
  },
  ledger: {
    message: h('Message', 'Which message this action was about: its own ID as the cloud knows it, and the reason it was dead-lettered. The entry keeps working even after the message has left the queue.'),
    who: h('Who', 'The person, rule or agent that started this action, and when. Where nobody signed in it says “from this browser session” rather than inventing a name.'),
    happened: h('What happened', 'Each step recorded for this action, in order, with the time. Every step is part of the tamper-evident chain.'),
    evidence: h('Evidence', 'The entry’s ID, the recovery ID stamped on the replayed message (how a return is recognised) and the hashes that chain it to the entry before. Verify the chain checks the whole ledger, not just this row.'),
  },
  signatures: {
    worse: h('Is it getting worse?', 'Messages per day for this signature over the chosen window, oldest on the left. It is flagged Growing when the latest half has at least 3 messages and at least twice the earlier half.'),
    helps: h('Does replaying help?', 'Judged only from replays that could be checked afterwards: it helps when at least half of those stayed fixed. Replays that could not be verified count for neither side.'),
    onItsOwn: h('Can ServiceHub replay it on its own?', 'Whether this kind of failure has earned automatic replay. Trust is earned from verified outcomes only, and a cloud that cannot prove a fix held never earns it — a person always decides there.'),
    seenIn: h('Seen in', 'The connected namespaces holding messages with this signature, production first, with how many each.'),
    where: h('Where', 'Places to act on this signature. Nothing is changed from here: replaying opens Home, and a rule is created on Auto Replay where it can be tested first.'),
  },
  message: {
    body: h('Message body', 'The content of the message as stored. Large bodies are cut to a preview, and it says when that happened. Reading it changes nothing.'),
    others: h('Others like it', 'Other dead letters in this queue with the same recorded reason. If there are many, the cause is probably shared.'),
    details: h('Details', 'The cloud’s own identifiers and timings for this message. Copy the ID to find the same message in your cloud’s console.'),
    properties: h('Properties', 'The application properties the sender attached. They often show which system, customer or order the message belongs to.'),
    headers: h('Headers', 'Transport information: content type, correlation and session IDs. Useful for following one message through several systems.'),
    delivery: h('Delivery', 'How many times a consumer picked the message up, and when it was first set aside. A dash means this cloud does not report it.'),
  },
  bulk: {
    grouped: h('Grouped by how they failed', 'The selected messages counted by their recorded failure reason, with how many of each will be replayed and how many are held back by the safety checks.'),
    where: h('Where each one goes', 'One row per message so you can check what is about to be sent, and to which queue or topic, before anything runs. Ten rows a page.'),
    run: h('How it will run', 'The pace (messages a second) and when the run stops by itself. Both are chosen to be gentle on the consumer that failed.'),
    after: h('After they are sent back', 'ServiceHub keeps watching each replayed message. Where the cloud can prove a fix held, that builds track record; where it cannot, it is recorded as Unverified — never as success.'),
  },
  approve: {
    why: h('Why it asked instead of acting', 'The safety check that made the Agent stop and ask. Nothing is sent until a person approves, and declining deletes nothing.'),
    waiting: h('Waiting for you', 'Each replay the Agent is holding, named by the message’s own ID, its queue and what went wrong. Tick the ones to approve; “select all” covers every one, shown or not.'),
    checks: h('Safety checks', 'The same checks every replay passes. A warning does not block, but says what cannot be promised — for example that this cloud cannot prove the fix held.'),
  },
  home: {
    needsYou: h('Needs your attention', 'Replays the Agent is holding for a person, rules that stopped themselves and agents that stopped reporting. Review opens the answer in place.'),
    recentAll: h('Recent activity, every cloud', 'The latest actions ServiceHub recorded across all connected clouds — connections, replays, decisions — with who did them.'),
    subscriptions: h('Subscriptions', 'The subscriptions under a topic. A topic holds nothing itself; its subscriptions do, so dead letters are counted per subscription. Use Look now to record their counts.'),
  },
  settings: {
    connections: h('Connections', 'The cloud accounts ServiceHub reads. Credentials are stored encrypted and never shown again; removing one stops the watching but keeps history.'),
    notifications: h('Notifications', 'Where ServiceHub tells you when something needs a person — in the app, and by webhook to your own tools.'),
    preferences: h('Preferences', 'How this browser shows ServiceHub: theme, rows per page and similar. They affect only you, only here.'),
    access: h('Access & security', 'Who may do what, and how requests are authenticated. A role limits actions; it never hides information you are already allowed to read.'),
    backup: h('Backup', 'A checked copy of ServiceHub’s whole database. It does not contain the encryption key, so keep the key safe separately.'),
  },
} as const satisfies Record<string, Record<string, ColumnHelp>>
