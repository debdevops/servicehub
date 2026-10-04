import type { Agent } from '../../api/agents'
import type { AuditEntry } from '../../api/identity'
import type { InsightFinding } from '../../api/insights'
import type { MessageGist } from '../../api/messageGist'
import type { CloudProvider, EnvironmentKind, Namespace, ProviderCapabilities } from '../../api/namespaces'
import type { EntryState } from '../../api/recovery'
import type { ReplayActor } from '../../api/replay'
import type { DemoDeadLetter, DemoEntity, DemoEntry, DemoRule, Story, World } from './model'
import { hashOf, rng, type Rng } from './random'

/**
 * The demo's made-up world, built from a scenario rather than typed in row by row.
 *
 * Three invented companies, one per cloud, each with a dev and a test namespace, and the same five failure stories on every
 * cloud — so a visitor learns what ServiceHub tells apart: an outage a replay cures, a token that keeps expiring, a schema
 * change a replay cannot fix, a poison message, and timeouts that are still arriving.
 *
 * Two rules hold everywhere:
 *  - Each cloud keeps its REAL capability differences (the same values the server's ProviderCapabilities holds). Azure can
 *    prove a fix held; AWS and Google cannot, so nothing here is ever "verified" on them.
 *  - Every name is invented. No real namespace, account, project or person appears.
 */
export const WORLD_VERSION = 3
const SEED = 20261004

const M = 60_000
const H = 60 * M
const D = 24 * H

/** How long a replay is "watched" in the demo before its outcome is known. The real product watches for hours. */
export const DEMO_WATCH_MS = 30_000
/** What the real watch window would be — shown on the watch card. */
export const REAL_WATCH_HOURS = 24

export const capabilities: Readonly<Record<CloudProvider, ProviderCapabilities>> = {
  azure: { supportsMessageCounts: true, supportsManualDeadLetter: true, supportsPurge: false, supportsScheduledMessages: true, supportsRepeatablePeek: true, supportsRecoveryMarker: true, canProveDlqAbsence: true, supportsTopics: true, supportsSubscriptions: true, notes: 'Purge is not supported — the SDK has no reliable single-message delete by sequence number.' },
  aws: { supportsMessageCounts: true, supportsManualDeadLetter: true, supportsPurge: true, supportsScheduledMessages: false, supportsRepeatablePeek: false, supportsRecoveryMarker: true, canProveDlqAbsence: false, supportsTopics: true, supportsSubscriptions: true, notes: 'SQS has no non-destructive peek, so every look is a receive.' },
  gcp: { supportsMessageCounts: false, supportsManualDeadLetter: false, supportsPurge: true, supportsScheduledMessages: false, supportsRepeatablePeek: false, supportsRecoveryMarker: true, canProveDlqAbsence: false, supportsTopics: true, supportsSubscriptions: true, notes: 'Pub/Sub has no count API; every pull counts as a delivery attempt.' },
}

/** The few invented people who appear in the demo's history. `you` is whoever is looking at it. */
export const people = {
  you: { identity: 'demo.visitor', kind: 'user', label: 'You', isSession: false },
  maya: { identity: 'maya.okafor', kind: 'user', label: 'Maya Okafor', isSession: false },
  daniel: { identity: 'daniel.reyes', kind: 'user', label: 'Daniel Reyes', isSession: false },
  priya: { identity: 'priya.nair', kind: 'user', label: 'Priya Nair', isSession: false },
  bot: { identity: 'ApiKey:ops-bot', kind: 'apiKey', label: 'ops-bot', isSession: false },
  auto: { identity: 'System:AutoReplayAgent', kind: 'automation', label: 'Auto Replay', isSession: false },
} as const satisfies Record<string, ReplayActor>

interface StoryDef {
  /** What Azure records as the dead-letter reason. AWS and Google record none. */
  readonly reason: string
  readonly error: string
  /** An error attribute the message itself carries — the only reading AWS and Google ever get. Null where it carries none. */
  readonly carried: string | null
  /** Whether replaying actually fixes it. Outcomes follow the story, never luck. */
  readonly curable: boolean
  readonly title: string
}

export const stories: Readonly<Record<Story, StoryDef>> = {
  outage: { reason: 'MaxDeliveryCountExceeded', error: 'InventoryService returned 503 Service Unavailable', carried: null, curable: true, title: 'Inventory outage' },
  token: { reason: 'UnauthorizedAccess', error: '401 Unauthorized: the access token has expired', carried: 'TokenExpiredException', curable: true, title: 'Expired token' },
  schema: { reason: 'ValidationFailed', error: 'missing required field: customerId', carried: 'SchemaValidationException: missing required field customerId', curable: false, title: 'Missing customerId' },
  poison: { reason: 'DeserializationFailed', error: 'Unexpected token < in JSON at position 0', carried: null, curable: false, title: 'Malformed message' },
  timeouts: { reason: 'MaxDeliveryCountExceeded', error: 'TimeoutException: the operation did not complete within 00:00:30', carried: null, curable: true, title: 'Timeouts' },
}

interface CloudDef {
  readonly provider: CloudProvider
  readonly company: string
  readonly slug: string
  readonly authType: Namespace['authType']
  readonly region: string | null
  /** Which queue or subscription each story lives on — one each, so the stories stay separate failures on every cloud. */
  readonly storyEntity: Readonly<Record<Story, string>>
  readonly topics: readonly string[]
  readonly thing: 'order' | 'payment' | 'event'
}

const clouds: readonly CloudDef[] = [
  {
    provider: 'azure', company: 'Contoso Orders', slug: 'contoso-orders', authType: 'managedIdentity', region: null, thing: 'order',
    storyEntity: { outage: 'orders', token: 'invoices', schema: 'order-events/subscriptions/billing', poison: 'shipments', timeouts: 'order-events/subscriptions/notifications' },
    topics: ['order-events'],
  },
  {
    provider: 'aws', company: 'Acme Payments', slug: 'acme-payments', authType: 'awsIamRole', region: 'eu-west-1', thing: 'payment',
    storyEntity: { outage: 'payments', token: 'payouts', schema: 'refunds', poison: 'stock-updates', timeouts: 'settlement-requests' },
    topics: ['payment-events'],
  },
  {
    provider: 'gcp', company: 'Globex Events', slug: 'globex-events', authType: 'gcpWorkloadIdentity', region: null, thing: 'event',
    storyEntity: { outage: 'signup-events/subscriptions/crm-sync', token: 'signup-events/subscriptions/welcome-email', schema: 'clickstream/subscriptions/analytics', poison: 'clickstream/subscriptions/sessionizer', timeouts: 'invoice-events/subscriptions/pdf-renderer' },
    topics: ['signup-events', 'clickstream', 'invoice-events'],
  },
]

export const signatureOf = (provider: CloudProvider, story: Story, entity: string) =>
  hashOf(`${provider}|${capabilities[provider].canProveDlqAbsence ? stories[story].reason : 'Unknown'}|${entity}`)

const topicOf = (entity: string) => (entity.includes('/subscriptions/') ? entity.split('/')[0] : null)

/** The body a dead letter carries. Made from the row, so nothing has to be stored. */
export function bodyOf(d: Pick<DemoDeadLetter, 'id' | 'story' | 'namespaceId' | 'enqueuedTimeUtc'>, provider: CloudProvider): string {
  if (d.story === 'poison') return '<html><head><title>502 Bad Gateway</title></head><body><h1>502 Bad Gateway</h1></body></html>'
  const n = (d.id * 7919) % 100000
  const amount = ((d.id * 1337) % 90000) / 100 + 9.99
  const customerId = d.story === 'schema' ? undefined : `CUS-${10000 + ((d.id * 31) % 89999)}`
  const thing = clouds.find((c) => c.provider === provider)!.thing
  const body =
    thing === 'order'
      ? { orderId: `ORD-${n}`, customerId, amount: amount.toFixed(2), currency: 'EUR', items: 1 + (d.id % 4), placedAt: d.enqueuedTimeUtc }
      : thing === 'payment'
        ? { paymentId: `PAY-${n}`, orderId: `ORD-${(n * 3) % 100000}`, customerId, amount: amount.toFixed(2), currency: 'EUR', method: ['card', 'sepa', 'wallet'][d.id % 3] }
        : { eventId: `evt_${n}`, userId: customerId?.replace('CUS', 'usr'), type: ['signup.completed', 'page.viewed', 'invoice.requested'][d.id % 3], occurredAt: d.enqueuedTimeUtc }
  return JSON.stringify(body, null, 2)
}

/** The short reading of a message shown in lists. Made on read, like the body, so the stored world stays small. */
export function gistOf(d: DemoDeadLetter, provider: CloudProvider): MessageGist {
  const preview = bodyOf(d, provider).replace(/\s+/g, ' ')
  return { preview: preview.length > 140 ? `${preview.slice(0, 140)}…` : preview, contentType: d.story === 'poison' ? 'text/html' : 'application/json', correlationId: d.correlationId, sessionId: null, properties: propertiesOf(d) }
}

/** The application properties a dead letter carries, as the API stores them. */
export function propertiesOf(d: Pick<DemoDeadLetter, 'id' | 'story'>): Record<string, string> {
  const carried = stories[d.story].carried
  return { source: ['checkout', 'mobile-app', 'partner-api'][d.id % 3], schemaVersion: d.story === 'schema' ? '3' : '2', ...(carried ? { errorType: carried } : {}) }
}

function agents(now: number): Agent[] {
  const iso = (msAgo: number) => new Date(now - msAgo).toISOString()
  const make = (a: Pick<Agent, 'id' | 'name' | 'purpose' | 'kind' | 'authority' | 'cadenceSeconds' | 'may' | 'mayNot'>, examined: number, changed: number, summary: string): Agent => ({
    ...a, canAct: a.kind === 'act', notes: null, health: 'healthy', late: false, isPaused: false,
    lastRunUtc: iso(Math.min(a.cadenceSeconds, 40) * 1000), lastResult: { examined, changed, summary, degraded: false }, lastFailure: null, consecutiveFailures: 0,
  })
  return [
    make({ id: 'dlq-monitor', name: 'Dead-letter Monitor', purpose: "Looks in every connected cloud's dead-letter queues, keeps a lasting list of what is stuck there, and notices when a message is gone.", kind: 'watch', authority: 'observes', cadenceSeconds: 10, may: ["Read every connected Azure queue's dead letters and keep a list of them", 'Group new dead letters by how they failed', 'Notice when a dead letter has gone from the queue'], mayNot: ['Replay, move or delete any message', 'Look at AWS or Google Cloud on its own — only when a person asks', 'Say a queue is empty when it could not read it'] }, 7, 1, 'Looked at 7 queues and subscriptions on Azure'),
    make({ id: 'recovery-verification', name: 'Replay Verifier', purpose: 'Watches each replayed message for the length of its watch window, and says whether it came back, stayed fixed, or cannot be proven.', kind: 'watch', authority: 'observes', cadenceSeconds: 60, may: ["Close a replay's watch window with what happened", 'Record that a replay stayed fixed where the cloud can prove it'], mayNot: ['Replay or touch any message', "Call a replay 'verified' where the cloud cannot prove it — it says 'verification required'"] }, 2, 0, '2 replays are being watched'),
    make({ id: 'bulk-replay', name: 'Bulk Replay', purpose: 'Sends back the dead letters a person chose and previewed, one at a time, each checked and recorded on its own.', kind: 'act', authority: 'actsWithApproval', cadenceSeconds: 2, may: ['Replay the messages a person previewed and started, one at a time', 'Stop after five failures in a row'], mayNot: ['Start a run by itself', 'Replay anything the safety checks refuse — each message is checked on its own'] }, 0, 0, 'No run is waiting'),
    make({ id: 'auto-replay', name: 'Auto Replay', purpose: 'Retries the failures your rules name, on its own, only where the safety checks say a fix can be trusted — and stops itself when fewer than half stay fixed.', kind: 'act', authority: 'actsAutonomously', cadenceSeconds: 30, may: ["Replay messages matching a rule that's turned on, once that failure has earned it"], mayNot: ['Replay in a Production namespace — never', "Replay where the cloud can't prove the fix held — it asks you instead", 'Turn a rule back on after it stopped itself'] }, 9, 1, 'Replayed 1 message; asked a person about the rest'),
    make({ id: 'autonomy-evaluation', name: 'Trust Evaluator', purpose: 'Counts how often replaying each kind of failure really fixed it, and decides whether that failure has earned automatic replay — or has lost it.', kind: 'decide', authority: 'proposes', cadenceSeconds: 3600, may: ['Let a failure be replayed without asking once its verified record earns it', 'Take that back as soon as the record falls'], mayNot: ['Replay anything itself', 'Skip a step, or promote in a Production namespace', 'Promote where the cloud cannot prove a fix held', 'Use AI or guesses — only counted outcomes'] }, 15, 0, 'Checked 15 kinds of failure; nothing changed'),
    make({ id: 'insights-anomaly', name: 'Spike Watcher', purpose: 'Notices when a queue is dead-lettering far more than it usually does.', kind: 'watch', authority: 'observes', cadenceSeconds: 300, may: ['Compare each queue with its own recent past'], mayNot: ['Replay, purge or change anything', 'Open a rule or change what a failure may do on its own', 'Use AI or guesses'] }, 21, 1, '1 queue is failing more than usual'),
    make({ id: 'insights-backlog', name: 'Backlog Forecaster', purpose: 'Says when a growing dead-letter queue will reach a size worth worrying about.', kind: 'watch', authority: 'observes', cadenceSeconds: 600, may: ['Project a growing queue forward from its own history'], mayNot: ['Replay, purge or change anything', 'Open a rule or change what a failure may do on its own', 'Use AI or guesses'] }, 21, 1, '1 queue is growing'),
    make({ id: 'insights-correlation', name: 'Pattern Linker', purpose: 'Notices when the same failure shows up in more than one place at once.', kind: 'watch', authority: 'observes', cadenceSeconds: 600, may: ['Compare failures across queues and clouds'], mayNot: ['Replay, purge or change anything', 'Open a rule or change what a failure may do on its own', 'Use AI or guesses'] }, 15, 1, '1 failure is happening in more than one place'),
    make({ id: 'insights-narration', name: 'Narrator', purpose: 'Writes one plain sentence about what changed since yesterday.', kind: 'watch', authority: 'observes', cadenceSeconds: 3600, may: ['Summarise counted changes in words'], mayNot: ['Replay, purge or change anything', 'Open a rule or change what a failure may do on its own', 'Use AI or guesses'] }, 3, 1, 'Wrote 3 summaries'),
  ]
}

/** Builds the whole world. The same input always gives the same world; only the timestamps move with `now`. */
export function buildWorld(now: number = Date.now()): World {
  const r: Rng = rng(SEED)
  const iso = (msAgo: number) => new Date(now - msAgo).toISOString()

  const namespaces: Namespace[] = []
  const entities: Record<string, DemoEntity[]> = {}
  const deadLetters: DemoDeadLetter[] = []
  const entries: DemoEntry[] = []
  const rules: DemoRule[] = []
  const audit: AuditEntry[] = []
  const insights: InsightFinding[] = []
  const unseen: Record<string, number> = {}
  const next = { deadLetter: 1, entry: 1, rule: 1, audit: 1, channel: 3, grant: 6, bulk: 1 }
  let sequence = 48_200

  const record = (msAgo: number, actor: ReplayActor, action: string, ns: Namespace | null, resource: string | null, outcome: 'Success' | 'Failure' = 'Success') => {
    audit.push({
      id: `demo-audit-${next.audit++}`, timestamp: iso(msAgo), actor, action, outcome, namespaceId: ns?.id ?? null, namespaceName: ns?.displayName ?? null,
      cloudProvider: ns ? { azure: 'Azure', aws: 'Aws', gcp: 'Gcp' }[ns.provider] : null, environment: ns?.environment ?? null, resourceName: resource, errorDetails: null, correlationId: null,
    })
  }

  const messageId = (provider: CloudProvider) =>
    provider === 'gcp' ? String(11_000_000_000_000 + r.int(0, 899_999_999_999)) : `${r.hex(8)}-${r.hex(4)}-4${r.hex(3)}-${r.pick(['8', '9', 'a', 'b'])}${r.hex(3)}-${r.hex(12)}`

  /** `count` dead letters of one story, enqueued over `spanMs` starting `startAgoMs` ago. */
  const burst = (cloud: CloudDef, ns: Namespace, story: Story, count: number, startAgoMs: number, spanMs: number): DemoDeadLetter[] => {
    const entity = cloud.storyEntity[story]
    const def = stories[story]
    const watched = capabilities[cloud.provider].supportsRepeatablePeek
    const rows: DemoDeadLetter[] = []
    for (let i = 0; i < count; i++) {
      const enqueuedAgo = Math.max(2 * M, startAgoMs - (spanMs * (i + r.next())) / count)
      // A watched cloud notices within minutes. An unwatched one only when a person looks.
      const detectedAgo = Math.max(M, enqueuedAgo - (watched ? r.int(1, 3) * M : Math.min(r.int(10, 90) * M, enqueuedAgo - M)))
      const id = next.deadLetter++
      const row: DemoDeadLetter = {
        id, namespaceId: ns.id, messageId: messageId(cloud.provider),
        sequenceNumber: watched ? sequence++ : r.int(1_000_000_000, 8_999_999_999_999),
        entityName: entity, entityType: topicOf(entity) ? 'subscription' : 'queue', topicName: topicOf(entity),
        detectedAtUtc: iso(detectedAgo), enqueuedTimeUtc: iso(enqueuedAgo),
        deliveryCount: cloud.provider === 'gcp' ? 0 : cloud.provider === 'aws' ? r.int(3, 5) : def.reason === 'MaxDeliveryCountExceeded' ? 10 : r.int(1, 3),
        sizeInBytes: story === 'poison' ? 96 : r.int(180, 420),
        deadLetterReason: watched ? def.reason : null, deadLetterErrorDescription: watched ? def.error : null,
        status: 'active', resolvedAt: null, resolutionCause: null, gist: null,
        story, signatureHash: signatureOf(cloud.provider, story, entity), correlationId: `corr-${r.hex(10)}`,
      }
      rows.push(row)
      deadLetters.push(row)
    }
    return rows
  }

  /** One ledger entry for `row`, and the state the dead letter is left in because of it. */
  const act = (cloud: CloudDef, row: DemoDeadLetter, o: { kind?: DemoEntry['kind']; actor: ReplayActor; agoMs: number; state: EntryState; ruleId?: number; level?: DemoEntry['level']; open?: boolean }): DemoEntry => {
    const n = next.entry++
    const kind = o.kind ?? 'Replay'
    const closed = !o.open
    const entry: DemoEntry = {
      id: `demo-entry-${String(n).padStart(4, '0')}`, operationId: `demo-op-${String(n).padStart(4, '0')}`, kind, dlqMessageId: row.id, namespaceId: row.namespaceId, provider: cloud.provider,
      entityName: row.entityName, targetEntity: row.topicName ?? row.entityName, messageId: row.messageId, signatureHash: row.signatureHash, story: row.story,
      begunAt: iso(o.agoMs), actor: o.actor, ruleId: o.ruleId ?? null, level: kind === 'Replay' ? (o.level ?? 'approve') : null,
      state: o.state, confidence: o.state === 'Returned' ? 'Exact' : null,
      windowEndsAt: kind === 'Replay' ? iso(o.agoMs - REAL_WATCH_HOURS * H) : null,
      // A returned replay is known as soon as the message is seen again; the rest close when the window does.
      closedAt: !closed ? null : o.state === 'Returned' ? iso(Math.max(0, o.agoMs - r.int(4, 12) * M)) : kind === 'Replay' ? iso(Math.max(0, o.agoMs - REAL_WATCH_HOURS * H)) : iso(o.agoMs),
      dueAt: null,
    }
    entries.push(entry)
    // A replay that came back is in the queue again. So is one on a cloud that cannot tell, when the cause was never fixed.
    const backInQueue = kind === 'Replay' && (o.state === 'Returned' || (o.state === 'Unverified' && !stories[row.story].curable))
    if (!backInQueue) {
      row.status = 'resolved'
      row.resolvedAt = entry.begunAt
      row.resolutionCause = kind === 'Replay' ? 'replayedByServiceHub' : kind === 'Purge' ? 'purgedByServiceHub' : 'declaredByOperator'
    }
    if (kind !== 'WriteOff') record(o.agoMs, o.actor, kind === 'Replay' ? 'Replay.Message' : 'Purge.Message', namespaces.find((x) => x.id === row.namespaceId) ?? null, row.entityName)
    return entry
  }

  for (const [index, cloud] of clouds.entries()) {
    const proves = capabilities[cloud.provider].canProveDlqAbsence
    const make = (environment: EnvironmentKind, suffix: string, n: number): Namespace => ({
      id: `demo-${cloud.provider}-0000-0000-0000000000${index + 1}${n}`, name: `${cloud.slug}-${suffix}`, displayName: `${cloud.company} (${suffix})`, description: 'Demo', provider: cloud.provider, environment,
      authType: cloud.authType, awsRegion: cloud.region, gcpProjectId: cloud.provider === 'gcp' ? `${cloud.slug}-${suffix}` : null, isActive: true,
      createdAt: iso((34 - index * 3) * D), lastConnectionTestAt: iso(r.int(8, 55) * M), lastConnectionTestSucceeded: true, capabilities: capabilities[cloud.provider],
    })
    const dev = make('dev', 'dev', 1)
    const test = make('uat', 'test', 2)
    namespaces.push(dev, test)
    record((34 - index * 3) * D, people.maya, 'Namespace.Connect', dev, dev.name)
    record((33 - index * 3) * D, people.maya, 'Namespace.Connect', test, test.name)

    const entityList = (storiesHere: readonly Story[]): DemoEntity[] => {
      const names = [...new Set(storiesHere.map((s) => cloud.storyEntity[s]))]
      // A topic is listed where one of its subscriptions is; AWS's topic has no dead letters of its own and is listed once.
      const topics = cloud.topics.filter((t) => names.some((n) => n.startsWith(`${t}/`)) || (cloud.provider === 'aws' && storiesHere.length > 3))
      const counts = capabilities[cloud.provider].supportsMessageCounts
      return [
        ...topics.map((t): DemoEntity => ({ name: t, kind: 'topic', activeMessages: null, deadLetterMessages: null, deadLetterTargetName: null })),
        ...names.map((n): DemoEntity => ({ name: n, kind: topicOf(n) ? 'subscription' : 'queue', activeMessages: counts ? r.int(3, 140) : null, deadLetterMessages: null, deadLetterTargetName: cloud.provider === 'aws' ? `${n}-dlq` : null })),
      ]
    }
    entities[dev.id] = entityList(['outage', 'token', 'schema', 'poison', 'timeouts'])
    entities[test.id] = entityList(['token', 'schema', 'timeouts'])

    const signature = (story: Story) => signatureOf(cloud.provider, story, cloud.storyEntity[story])
    const rule = (story: Story, name: string, agoDays: number, off?: { reason: 'CircuitBreaker' | 'Person'; detail: string }): DemoRule => {
      const made: DemoRule = {
        id: next.rule++, name, provider: cloud.provider, namespaceId: dev.id, reason: proves ? stories[story].reason : null, entityName: cloud.storyEntity[story], signatureHash: signature(story),
        maxPerHour: story === 'token' ? 30 : 120, waitSeconds: story === 'token' ? 120 : 60, backOff: true, enabled: !off, disabledReason: off?.reason ?? null, disabledDetail: off?.detail ?? null, updatedAt: iso(agoDays * D),
      }
      rules.push(made)
      record(agoDays * D + 5 * M, people.maya, 'Rule.Create', dev, name)
      return made
    }
    const outageRule = rule('outage', `${stories.outage.title} — ${cloud.storyEntity.outage}`, 8)
    const tokenRule = rule('token', `${stories.token.title} — ${cloud.storyEntity.token}`, 6)

    // Outage: once nine days ago (people replayed it), again two days ago.
    const firstOutage = burst(cloud, dev, 'outage', 35, 9 * D + 4 * H, 35 * M)
    firstOutage.forEach((row, i) => act(cloud, row, { actor: i % 3 === 0 ? people.priya : people.daniel, agoMs: 9 * D + 2 * H - i * M, state: proves ? 'Recovered' : 'Unverified' }))
    const secondOutage = burst(cloud, dev, 'outage', 60, 2 * D + 6 * H, 40 * M)
    if (proves) {
      // By then the failure had earned it: the rule replayed all of them on its own.
      secondOutage.forEach((row, i) => act(cloud, row, { actor: people.auto, agoMs: 2 * D + 4 * H - i * 30_000, state: 'Recovered', ruleId: outageRule.id, level: 'unattended' }))
    } else {
      // The rule may only ask. A person replayed twelve; the rest wait.
      secondOutage.slice(0, 12).forEach((row, i) => act(cloud, row, { actor: people.daniel, agoMs: 2 * D + 3 * H - i * 2 * M, state: 'Unverified' }))
    }

    // Expired token: comes round every few days.
    const tokenBursts = [12 * D, 8 * D, 4 * D].map((ago) => burst(cloud, dev, 'token', 8, ago, 12 * M))
    tokenBursts[0].forEach((row, i) => act(cloud, row, { actor: people.daniel, agoMs: 12 * D - 30 * M - i * M, state: proves ? 'Recovered' : 'Unverified' }))
    tokenBursts[1].forEach((row, i) => act(cloud, row, { actor: people.priya, agoMs: 8 * D - 25 * M - i * M, state: proves ? 'Recovered' : 'Unverified' }))
    tokenBursts[2].forEach((row, i) =>
      act(cloud, row, proves
        ? { actor: people.auto, agoMs: 4 * D - 14 * M - i * 2 * M, state: 'Recovered', ruleId: tokenRule.id, level: 'standing' }
        : { actor: people.priya, agoMs: 4 * D - 40 * M - i * M, state: 'Unverified' }))
    // …and again twenty minutes ago. On Azure the rule is working through these while the visitor watches.
    burst(cloud, dev, 'token', 8, 20 * M, 6 * M)

    // Schema change: growing every day since a release five days ago. Replaying does not help.
    const schema = [6, 9, 13, 18, 24].flatMap((count, day) => burst(cloud, dev, 'schema', count, (5 - day) * D - 2 * H, 9 * H))
    if (proves) {
      const schemaRule = rule('schema', `${stories.schema.title} — ${cloud.storyEntity.schema}`, 4, { reason: 'CircuitBreaker', detail: 'None of its last 20 replays stayed fixed, so it stopped itself.' })
      schema.slice(0, 20).forEach((row, i) => act(cloud, row, { actor: people.auto, agoMs: 4 * D - 3 * H - i * M, state: 'Returned', ruleId: schemaRule.id, level: 'approve' }))
    } else {
      rule('schema', `${stories.schema.title} — ${cloud.storyEntity.schema}`, 4, { reason: 'Person', detail: 'Turned off by Maya Okafor: replayed messages kept coming back.' })
      schema.slice(0, 5).forEach((row, i) => act(cloud, row, { actor: people.daniel, agoMs: 4 * D - 3 * H - i * 3 * M, state: 'Unverified' }))
    }

    // Poison messages: a handful, six days ago.
    const poison = burst(cloud, dev, 'poison', 6, 6 * D, 20 * M)
    if (capabilities[cloud.provider].supportsPurge) {
      poison.slice(0, 2).forEach((row, i) => act(cloud, row, { kind: 'Purge', actor: people.maya, agoMs: 5 * D + 20 * H - i * 4 * M, state: 'Discarded' }))
    } else {
      act(cloud, poison[0], { actor: people.daniel, agoMs: 5 * D + 22 * H, state: 'Returned' })
      act(cloud, poison[1], { kind: 'WriteOff', actor: people.maya, agoMs: 5 * D + 20 * H, state: 'WrittenOff' })
    }

    // Timeouts: started an hour ago and still arriving. Nobody has decided anything yet.
    burst(cloud, dev, 'timeouts', 25, 62 * M, 58 * M)
    unseen[dev.id] = proves ? 0 : 4

    // The test namespace: quieter, the same kinds of failure.
    const testToken = burst(cloud, test, 'token', 10, 4 * D + H, 15 * M)
    testToken.slice(0, 5).forEach((row, i) => act(cloud, row, { actor: people.priya, agoMs: 4 * D - i * 2 * M, state: proves ? 'Recovered' : 'Unverified' }))
    burst(cloud, test, 'schema', 12, 3 * D, 2 * D)
    burst(cloud, test, 'timeouts', 6, 50 * M, 40 * M)
    unseen[test.id] = 0

    // Every number in a finding is counted from the rows above, never typed in.
    const stuck = (ns: Namespace, story: Story) => deadLetters.filter((d) => d.namespaceId === ns.id && d.story === story && d.status === 'active').length
    const sinceYesterday = deadLetters.filter((d) => d.namespaceId === dev.id && now - Date.parse(d.detectedAtUtc) <= D).length
    const fixedSinceYesterday = entries.filter((e) => e.namespaceId === dev.id && e.state === 'Recovered' && e.closedAt && now - Date.parse(e.closedAt) <= D).length
    const rulesOff = rules.filter((x) => x.provider === cloud.provider && !x.enabled).length
    const growing = stuck(dev, 'schema')
    const finding = (kind: InsightFinding['kind'], severity: number, what: string, story: Story, firstAgo: number, metrics: Record<string, number>, cleared = false) =>
      insights.push({ id: insights.length + 1, kind, severity, what, entityName: cloud.storyEntity[story], namespaceId: dev.id, namespaceName: dev.displayName, provider: cloud.provider, metrics, firstSeenAt: iso(firstAgo), lastSeenAt: iso(cleared ? firstAgo - 3 * H : 4 * M), clearedAt: cleared ? iso(firstAgo - 3 * H) : null, suggestion: false })
    if (capabilities[cloud.provider].supportsRepeatablePeek) {
      finding('anomaly', 3, `${cloud.storyEntity.timeouts} is dead-lettering far more than usual: ${stuck(dev, 'timeouts')} in the last hour against a usual 1.`, 'timeouts', 55 * M, { lastHour: stuck(dev, 'timeouts'), usualPerHour: 1 })
      finding('backlog', 2, `${cloud.storyEntity.schema} has grown every day for five days and now holds ${growing}. At this rate it passes ${growing + 3 * 24} in three days.`, 'schema', 4 * D, { now: growing, perDay: 24, projected: growing + 3 * 24 })
      finding('correlation', 2, `“${stories.schema.error}” is happening in both ${dev.displayName} and ${test.displayName}.`, 'schema', 3 * D, { places: 2 })
      finding('narration', 1, `Since yesterday: ${sinceYesterday} new dead letters, ${fixedSinceYesterday} replayed and staying fixed, and ${rulesOff} rule${rulesOff === 1 ? ' is' : 's are'} switched off.`, 'timeouts', 20 * M, { new: sinceYesterday, fixed: fixedSinceYesterday })
      finding('anomaly', 3, `${cloud.storyEntity.outage} dead-lettered ${secondOutage.length} messages in 40 minutes.`, 'outage', 2 * D + 6 * H, { inWindow: secondOutage.length, usualPerHour: 0 }, true)
    } else {
      finding('narration', 1, `ServiceHub does not watch this cloud on its own. What it knows comes from the last time a person looked.`, 'timeouts', 2 * H, { looks: 3 })
      finding('correlation', 2, `The same failure on ${cloud.storyEntity.schema} was seen in both ${dev.displayName} and ${test.displayName}.`, 'schema', 3 * D, { places: 2 })
    }
  }

  audit.sort((a, b) => b.timestamp.localeCompare(a.timestamp))

  const azureDev = namespaces[0]
  return {
    version: WORLD_VERSION, builtAt: now, rng: r.state, namespaces, entities, deadLetters, entries, rules, declined: [],
    agents: agents(now), activity: {}, audit,
    channels: [
      { id: 'demo-channel-1', format: 'slack', label: '#orders-oncall', enabled: true, createdAt: iso(20 * D), createdBy: people.maya.label, lastDeliveredAt: iso(58 * M), lastError: null },
      { id: 'demo-channel-2', format: 'teams', label: 'Payments Ops', enabled: true, createdAt: iso(12 * D), createdBy: people.priya.label, lastDeliveredAt: iso(2 * D), lastError: null },
    ],
    emergencyStop: { active: false, by: null, at: null, reason: null },
    grants: [
      { id: 'demo-grant-1', granteeIdentity: people.you.identity, granteeKind: 'User', role: 'Admin', namespaceId: null, pillarKind: null, grantedAt: iso(34 * D), grantedByIdentity: people.maya.identity },
      { id: 'demo-grant-2', granteeIdentity: people.maya.identity, granteeKind: 'User', role: 'Admin', namespaceId: null, pillarKind: null, grantedAt: iso(34 * D), grantedByIdentity: people.maya.identity },
      { id: 'demo-grant-3', granteeIdentity: people.priya.identity, granteeKind: 'User', role: 'Approver', namespaceId: null, pillarKind: null, grantedAt: iso(30 * D), grantedByIdentity: people.maya.identity },
      { id: 'demo-grant-4', granteeIdentity: people.daniel.identity, granteeKind: 'User', role: 'Operator', namespaceId: azureDev.id, pillarKind: null, grantedAt: iso(28 * D), grantedByIdentity: people.maya.identity },
      { id: 'demo-grant-5', granteeIdentity: people.bot.identity, granteeKind: 'ApiKey', role: 'Viewer', namespaceId: null, pillarKind: null, grantedAt: iso(21 * D), grantedByIdentity: people.maya.identity },
    ],
    insights, bulk: {}, previews: {}, unseen, next, ticks: 0,
  }
}
