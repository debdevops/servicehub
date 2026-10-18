import type { Agent } from '../../lib/api/agents'
import type { ComponentEntity, ConnectedComponent, Count, LastLook } from '../../lib/api/components'
import type { CoverageRow } from '../../lib/api/coverage'
import type { EmergencyStop } from '../../lib/api/settings'
import type { Me, Role } from '../../lib/api/identity'

/** Every sentence and word on the 5.0.0 Home that is computed from data, in one place, so a test can read them and a screen cannot invent them. */

const n = (value: number) => value.toLocaleString('en-US')

/** A count as people read it. A number the cloud only estimates says so; one it does not report is never drawn as 0 (R5). */
export function countWords(count: Count): string {
  switch (count.kind) {
    case 'exact': return n(count.value ?? 0)
    case 'approximate': return `${n(count.value ?? 0)} (approx)`
    case 'notReported': return 'Can’t count here'
    case 'notRead': return 'Not read'
  }
}

/** What ServiceHub kept at the last look. `null` messages is "never looked", which is not the same as none. */
export function recordedWords(look: LastLook | null): string {
  if (look === null) return '—'
  if (look.messages === null) return 'Not looked yet'
  return look.reachedLimit ? `${n(look.messages)} (limit)` : n(look.messages)
}

/** The status word of a component row. Computed, never stored; "Can’t reach" is also shown in the Connection column. */
export function statusWords(c: Pick<ConnectedComponent, 'needsYou' | 'reachable'>): { text: string; tone: 'bad' | 'warn' | 'ok' } {
  if (c.needsYou > 0) return { text: 'Needs you', tone: 'bad' }
  if (!c.reachable) return { text: 'Can’t reach', tone: 'warn' }
  return { text: 'No items need attention', tone: 'ok' }
}

export interface HomeTotals {
  /** The first tile. `dead-letter now` when any component reports a number; `recorded` when none does. */
  readonly first: {
    readonly label: 'In dead-letter now' | 'Dead letters recorded'
    readonly value: number | null
    readonly approximate: boolean
    readonly recorded: number | null
    readonly components: number
    /** True when no number is shown because the read failed (not because the cloud reports none): the tile says "Can’t read". */
    readonly unreadable: boolean
  }
  readonly needsYou: number
  readonly approvals: number
  readonly handOffs: number
}

/**
 * The three tiles, summed over ONE cloud's components (R5: nothing is ever added across clouds). A cloud that reports no counts
 * says "Dead letters recorded" and shows what ServiceHub kept; it never shows the adapter's 0 as "in dead-letter now".
 */
export function homeTotals(components: readonly ConnectedComponent[]): HomeTotals {
  const counted = components.filter((c) => c.deadLetters.value !== null)
  const recorded = components.flatMap((c) => (c.recorded?.messages != null ? [c.recorded.messages] : []))
  const recordedSum = recorded.length ? recorded.reduce((a, b) => a + b, 0) : null
  const reports = counted.length > 0
  // A cloud that reports counts but could not be read is still "in dead-letter now": it is the read that failed, not the cloud's ability to count.
  const failedRead = !reports && components.some((c) => c.deadLetters.kind === 'notRead') && !components.some((c) => c.deadLetters.kind === 'notReported')
  return {
    first: {
      label: reports || failedRead ? 'In dead-letter now' : 'Dead letters recorded',
      unreadable: failedRead,
      value: reports ? counted.reduce((sum, c) => sum + (c.deadLetters.value ?? 0), 0) : failedRead ? null : recordedSum,
      approximate: counted.some((c) => c.deadLetters.kind === 'approximate'),
      recorded: reports ? recordedSum : null,
      components: components.length,
    },
    needsYou: components.reduce((sum, c) => sum + c.needsYou, 0),
    approvals: components.reduce((sum, c) => sum + c.approvals, 0),
    handOffs: components.reduce((sum, c) => sum + c.handOffs, 0),
  }
}

/** The sentences about how far this cloud's numbers can be trusted — one per fact that is true of them, never one about a cloud by name. */
function countSentences(cloudName: string, components: readonly ConnectedComponent[]): string[] {
  if (components.some((c) => c.deadLetters.kind === 'notReported')) {
    return [`${cloudName} reports no message counts, so the numbers are what ServiceHub recorded when you last looked.`]
  }
  const parts = [components.some((c) => c.deadLetters.kind === 'approximate') ? `Counts are what ${cloudName} reports, and are approximate.` : 'Counts are the latest values read by ServiceHub.']
  if (components.some((c) => !c.watched)) parts.push('Messages are recorded only when you look.')
  return parts
}

/** "What you’re looking at" on a cloud's Home. */
export function lookingAt(cloudName: string, components: readonly ConnectedComponent[]): string {
  return [`This page shows your connected ${cloudName} resources and the items that need your attention.`, ...countSentences(cloudName, components)].join(' ')
}

/** "What you’re looking at" on one connection's page. */
export function lookingAtOne(cloudName: string, component: ConnectedComponent): string {
  return [`One ${component.componentName} connection: its queues, topics and subscriptions, what is dead-lettered in each, and what is waiting for a person.`, ...countSentences(cloudName, [component])].join(' ')
}

/** The sentence under "Evidence and replay outcome", chosen by what the catalogue says about the component — never by cloud. */
export function evidenceSentence(row: Pick<CoverageRow, 'class' | 'status' | 'note' | 'offersReplay'>): { text: string; tone: 'good' | 'caution' | 'none' } {
  if (row.status === 'beingValidated' || row.class === 'r0Unknown') {
    return { text: 'Not yet validated. Replay outcomes end as Unverified until we confirm what can be proved in a live run.', tone: 'caution' }
  }
  if (row.class === 'r1ReplayableProvable') {
    return { text: 'Can reach Verified when a replayed message does not return to the dead-letter queue within the observation window.', tone: 'good' }
  }
  if (row.class === 'r2ReplayableUnproven') {
    return { text: `Replays end as Unverified: the cloud cannot show that a message did not return, so each replay is recorded as verification required, never as fixed.${row.note ? ` ${row.note}.` : ''}`, tone: 'caution' }
  }
  return { text: row.offersReplay ? 'Replay is offered.' : 'ServiceHub shows it and hands it to a person; it offers no replay.', tone: 'none' }
}

const family: Readonly<Record<string, string>> = {
  unverified: 'Unverified', rejected: 'Rejected', policyBlocked: 'Policy blocked', retryBudgetExhausted: 'Retry budget exhausted',
  noRecoveryPath: 'No recovery path', outcomeUnknown: 'Outcome unknown', notIdempotent: 'Not idempotent', noRetryTarget: 'No retry target',
  dependencyDown: 'Dependency down', poisonPayload: 'Poison payload', stalledLiveQueue: 'Stalled live queue', agentHealth: 'Agent health',
}

/** A family label in words. An unknown family is shown as the server sent it, spaced out; a missing one as no label. */
export function familyWords(value: string | null | undefined): string | null {
  if (!value) return null
  return family[value] ?? value.replace(/([A-Z])/g, ' $1').replace(/^./, (c) => c.toUpperCase())
}

// ── The header ──────────────────────────────────────────────────────────────────────────────────

/** The role shown in the header: the recovery role (what Review and Replay need), falling back to the fleet role. `null` while unknown. */
export function roleWords(me: Me | undefined): string | null {
  if (!me) return null
  const role: Role | null | undefined = me.recoverRole ?? me.effectiveRole
  return role ?? 'No role'
}

export interface AgentState {
  readonly status: 'running' | 'paused' | 'partly-paused' | 'unknown'
  readonly title: string
  readonly detail: string
}

/** The Agent in the header: from the agents that can act, never self-reported. Paused means it will not act until someone resumes it. */
export function agentState(agents: readonly Agent[] | undefined): AgentState {
  if (!agents) return { status: 'unknown', title: 'Agent', detail: 'Not read yet' }
  const acting = agents.filter((a) => a.canAct)
  if (acting.length === 0) return { status: 'unknown', title: 'Agent', detail: 'No acting agent is running' }
  const paused = acting.filter((a) => a.isPaused)
  if (paused.length === acting.length) return { status: 'paused', title: 'Agent paused', detail: 'Will not act until resumed' }
  if (paused.length > 0) return { status: 'partly-paused', title: 'Agent partly paused', detail: `${paused.map((a) => a.name).join(', ')} paused` }
  return { status: 'running', title: 'Agent running', detail: 'Watching and replaying only what it may' }
}

export type StopState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'off' }
  | { readonly kind: 'on'; readonly by: string | null; readonly at: string | null; readonly reason: string | null }
  | { readonly kind: 'unreadable' }

/**
 * The emergency stop as the header shows it. If the state cannot be read it is shown as ON: a stop nobody can see is worse than a
 * band that is too cautious, and the server enforces the real state either way.
 */
export function stopState(stop: EmergencyStop | undefined, failed: boolean): StopState {
  if (failed) return { kind: 'unreadable' }
  if (!stop) return { kind: 'loading' }
  return stop.active ? { kind: 'on', by: stop.by, at: stop.at, reason: stop.reason } : { kind: 'off' }
}

/** Needs-you split for one entity row of the table. */
export const entityNeeds = (e: ComponentEntity) => e.needsYou

// ── All clouds ──────────────────────────────────────────────────────────────────────────────────

/** How one cloud's dead-letter numbers can be read, in the words of the "no total across clouds" sentence. */
function numberKind(components: readonly ConnectedComponent[]): 'exact' | 'approximate' | 'reports none' | 'could not be read' {
  if (components.some((c) => c.deadLetters.kind === 'notReported')) return 'reports none'
  if (components.some((c) => c.deadLetters.kind === 'approximate')) return 'approximate'
  if (components.some((c) => c.deadLetters.kind === 'exact')) return 'exact'
  return 'could not be read'
}

/**
 * Why there is no dead-letter total across clouds, said from what each cloud's numbers actually are ("Azure counts are exact, AWS
 * counts are approximate, and Google Cloud reports none"). Null with fewer than two clouds: there is nothing to add up.
 */
export function noTotalSentence(clouds: readonly { readonly name: string; readonly components: readonly ConnectedComponent[] }[]): string | null {
  if (clouds.length < 2) return null
  const parts = clouds.map(({ name, components }) => {
    const kind = numberKind(components)
    return kind === 'reports none' ? `${name} reports none` : kind === 'could not be read' ? `${name} could not be read` : `${name} counts are ${kind}`
  })
  const said = parts.length === 2 ? parts.join(' and ') : `${parts.slice(0, -1).join(', ')}, and ${parts[parts.length - 1]}`
  return `${said}, so adding them would give a number that means nothing. Each cloud keeps its own.`
}

export interface CloudFacts {
  readonly watched: { readonly text: string; readonly good: boolean }
  readonly counts: { readonly text: string; readonly good: boolean }
  readonly evidence: { readonly text: string; readonly tone: 'good' | 'caution' | 'none' }
}

/** The three small facts under a cloud's card, from its components and what the catalogue says about them — never from its name. */
export function cloudFacts(components: readonly ConnectedComponent[], covered: readonly Pick<CoverageRow, 'class' | 'status'>[]): CloudFacts {
  const allWatched = components.length > 0 && components.every((c) => c.watched)
  const counts = components.some((c) => c.deadLetters.kind === 'exact' || c.deadLetters.kind === 'approximate')
  const evidence = covered.some((r) => r.status === 'supported' && r.class === 'r1ReplayableProvable')
    ? { text: 'Can reach Verified', tone: 'good' as const }
    : covered.some((r) => r.status === 'supported' && r.class === 'r2ReplayableUnproven')
      ? { text: 'Replays end Unverified', tone: 'caution' as const }
      : covered.length > 0 ? { text: 'Not yet validated', tone: 'caution' as const } : { text: 'No replay offered', tone: 'none' as const }
  return {
    watched: allWatched ? { text: 'Watched automatically', good: true } : { text: 'Recorded when you look', good: false },
    counts: counts ? { text: 'Counts messages', good: true } : { text: 'No message counts', good: false },
    evidence,
  }
}

// ── One connection's page ───────────────────────────────────────────────────────────────────────

const authWords: Readonly<Record<string, string>> = {
  connectionString: 'Connection string', managedIdentity: 'Managed identity', servicePrincipal: 'Service principal', defaultAzureCredential: 'Default Azure credential',
  awsAccessKey: 'Access key', awsIamRole: 'IAM role', awsOidc: 'OIDC federation', gcpServiceAccount: 'Service account key', gcpWorkloadIdentity: 'Workload identity',
}

/** How the connection signs in, in words. Never the credential itself, and an unknown type is shown as stored rather than guessed. */
export const credentialWords = (authType: string): string => authWords[authType] ?? authType

/** What the connection's counts are, for the "Counts" line of Component information. */
export function countsWords(cloudName: string, count: Count): string {
  switch (count.kind) {
    case 'exact': return 'Exact'
    case 'approximate': return `Approximate, as ${cloudName} reports them`
    case 'notReported': return `Not reported by ${cloudName}`
    case 'notRead': return 'Not read'
  }
}

// ── Recovery ────────────────────────────────────────────────────────────────────────────────────

export interface OutcomeWords {
  /** The outcome word: Verified, Unverified, Came back, Watching, Not sent, Outcome unknown. */
  readonly word: string
  readonly tone: 'good' | 'caution' | 'bad' | 'neutral'
  /** "What was observed", in a sentence — only what was in fact observed, never what was hoped. */
  readonly observed: string
}

/**
 * How a replay ended, in the product's four outcome words (plan §0 rule 9). "Verified" means the message did not come back; it is
 * never "fixed" and never "succeeded". Where the cloud cannot show that, the word is "Unverified", and the sentence says why.
 */
export function outcomeWords(v: { status: string; confidence: 'Exact' | 'Heuristic' | null; watchUntil: string | null }, formatTime: (iso: string) => string): OutcomeWords {
  switch (v.status) {
    case 'verified':
      return { word: 'Verified', tone: 'good', observed: 'Did not return in the window' }
    case 'verification_required':
      return { word: 'Unverified', tone: 'caution', observed: 'Sent. This cloud cannot show whether it came back, so it is not called verified' }
    case 'returned':
      return { word: 'Came back', tone: 'bad', observed: `Returned to the dead-letter queue (matched by ${v.confidence === 'Heuristic' ? 'its contents' : 'its recovery ID'})` }
    case 'not_sent':
      return { word: 'Not sent', tone: 'bad', observed: 'Nothing was sent. The original is still in the dead-letter queue' }
    case 'unknown':
      return { word: 'Outcome unknown', tone: 'caution', observed: 'Contact was lost mid-call, so it is not known whether it was sent' }
    default:
      return { word: 'Watching', tone: 'neutral', observed: v.watchUntil ? `Watching for it to fail again until ${formatTime(v.watchUntil)}` : 'Watching for it to fail again' }
  }
}

export const recoveryWindows = [
  { id: '24h', label: 'Last 24 hours' }, { id: '7d', label: 'Last 7 days' }, { id: '30d', label: 'Last 30 days' }, { id: 'all', label: 'All time' },
] as const
export type RecoveryWindowId = (typeof recoveryWindows)[number]['id']

/** The sentence under the table: what the best outcome word means on this connection, said from whether its cloud can prove it. */
export function outcomeFootnote(canProve: boolean): string {
  return canProve
    ? 'Verified means the message did not come back to the dead-letter queue within the observation window. It does not mean the consumer finished its work.'
    : 'This cloud cannot show that a replayed message did not come back, so a replay ends Unverified, never Verified. Unverified does not mean it failed. It means ServiceHub cannot say either way.'
}

// ── Configuration ───────────────────────────────────────────────────────────────────────────────

/** How the notification channels are described on a connection's page: every enabled channel, since a channel has no component (F2, option A). */
export function channelsWords(channels: readonly { label: string; enabled: boolean }[] | undefined): string {
  if (channels === undefined) return 'Not read'
  const on = channels.filter((c) => c.enabled)
  if (on.length === 0) return 'No channel is set up, so only the bell tells you'
  return `Every enabled channel (${on.length}): ${on.map((c) => c.label).join(', ')}`
}

/** Where fix confirmation stands for a cloud that cannot confirm a fix by itself, in the words of its own status. */
export function observerWords(o: { enabled: boolean; live: boolean; lastConfirmedAt: string | null }, formatTime: (iso: string) => string): string {
  if (!o.enabled) return 'Not set up'
  if (o.live) return `Live${o.lastConfirmedAt ? `, last confirmed ${formatTime(o.lastConfirmedAt)}` : ''}`
  return `Set up, but not live${o.lastConfirmedAt ? `: last confirmed ${formatTime(o.lastConfirmedAt)}` : ': never confirmed yet'}`
}
