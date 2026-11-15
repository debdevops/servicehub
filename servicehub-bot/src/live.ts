/**
 * Live numbers for an answer, read from ServiceHub as the person asking (see `servicehub.ts`: GET only, fixed paths).
 *
 * Rules, from ServiceHub's own build plan:
 * - Every number has a source, and a count is never shown as exact when the cloud did not say so ("about", "can't count").
 * - Totals are never added across clouds; each connection is shown on its own.
 * - Words come from the product: Look now, Dead-letter messages, Pending approvals, Replay history, and the outcome words
 *   Watching / Verified / Came back / Unverified / Not sent / Outcome unknown.
 * - What a component can do comes from ServiceHub's coverage list (`offersReplay`), never from a cloud's name.
 * - No message body or preview is ever printed: only counts, reasons, queue names and states.
 */
import type { LiveProvider } from './answer.ts'
import type { ServiceHubReader } from './servicehub.ts'
import type { LiveIntent, Link } from './types.ts'

// ---- The parts of ServiceHub's responses the bot reads (shapes copied from the running API, camelCase on the wire) ----

type CountKind = 'exact' | 'approximate' | 'notReported' | 'notRead'
interface Count { readonly value: number | null; readonly kind: CountKind }
interface LastLook { readonly messages: number | null; readonly reachedLimit: boolean; readonly atUtc: string | null }
interface ComponentEntity { readonly name: string; readonly parent: string | null; readonly deadLetters: Count }
export interface ConnectedComponent {
  readonly namespaceId: string
  readonly componentKey: string
  readonly cloud: string
  readonly name: string
  readonly componentName: string
  readonly environment: string
  readonly canSend: boolean
  readonly reachable: boolean
  readonly lastError: string | null
  readonly watched: boolean
  readonly deadLetters: Count
  readonly recorded: LastLook | null
  readonly approvals: number
  readonly handOffs: number
  readonly entities: readonly ComponentEntity[]
}
export interface ComponentOverview { readonly components: readonly ConnectedComponent[] }
/** One stored connection from `GET /namespaces`. It holds no credential; only these fields are read. */
export interface Connection {
  readonly id: string
  readonly name: string
  readonly displayName: string | null
  readonly provider: string
  readonly environment: string
  readonly isActive: boolean
  readonly lastConnectionTestAt: string | null
  readonly lastConnectionTestSucceeded: boolean | null
}
export interface CoverageRow { readonly componentKey: string; readonly cloud: string; readonly cloudName: string; readonly offersReplay: boolean }
export interface Coverage { readonly rows: readonly CoverageRow[] }
export interface DeadLetterList {
  readonly paging: { readonly total: number }
  readonly groups: readonly { readonly reason: string | null; readonly count: number }[]
  readonly otherReasons: { readonly count: number; readonly kinds: number } | null
}
interface StateCount { readonly state: string; readonly count: number }
export interface RecoverySummary {
  readonly window: string
  readonly total: number
  readonly states: readonly StateCount[]
  readonly byProvider: readonly { readonly provider: string; readonly total: number; readonly states: readonly StateCount[]; readonly stayedFixedRate: number | null }[]
  readonly stayedFixedRate: number | null
}

/** How many connections an answer lists before it says "and N more". */
const MAX_CONNECTIONS = 5
/** The app's value for "messages with no recorded reason" in the `reason` filter. */
const NO_REASON = '__none__'
const SUMMARY_WINDOW = '7d'

export function createLiveProvider(reader: ServiceHubReader): LiveProvider {
  return async (intent: LiveIntent) => {
    switch (intent) {
      case 'dlq-replay':
        return deadLetters(reader, 'replay')
      case 'dlq-status':
        return deadLetters(reader, 'status')
      case 'replay-results':
        return replayResults(reader)
      case 'connections':
        return connections(reader)
    }
  }
}

type LiveResult = { markdown: string; links: Link[] }

// ---- Dead letters: what is stuck now, and (for replay) where to go and what to fix first ----

async function deadLetters(reader: ServiceHubReader, focus: 'replay' | 'status'): Promise<LiveResult> {
  const [overview, coverage] = await Promise.all([
    reader.get<ComponentOverview>('/fleet/components'),
    reader.get<Coverage>('/fleet/coverage'),
  ])
  if (overview.components.length === 0) return notConnected()

  const replayable = new Set(coverage.rows.filter((r) => r.offersReplay).map((r) => `${r.cloud}:${r.componentKey}`))
  const cloudName = cloudNames(coverage)
  const stores = overview.components.filter((c) => replayable.has(`${c.cloud}:${c.componentKey}`))
  if (stores.length === 0) {
    return {
      markdown: '**Right now:** none of your connected components keeps dead-lettered messages that ServiceHub can replay. **What ServiceHub covers** lists which ones can.',
      links: [{ label: 'What ServiceHub covers', href: '/coverage' }],
    }
  }

  // Reasons are recorded per connection. One read each, in parallel; a failed read only loses that connection's reasons.
  const namespaceIds = [...new Set(stores.map((c) => c.namespaceId))]
  const reasonLists = await Promise.all(
    namespaceIds.map((id) => reader.get<DeadLetterList>('/dead-letters', { namespaceId: id, status: 'active', pageSize: 1 }).catch(() => null)),
  )
  const reasonsOf = new Map(namespaceIds.map((id, i) => [id, reasonLists[i] ?? null]))

  const ordered = [...stores].sort((a, b) => sizeOf(b) - sizeOf(a))
  const shown = ordered.slice(0, MAX_CONNECTIONS)
  const lines = ['**Right now** (live from your ServiceHub):']
  const links: Link[] = []
  const tips = new Set<string>()

  for (const c of shown) {
    const reasons = reasonsOf.get(c.namespaceId) ?? null
    lines.push(`- **${c.name}** (${cloudName(c.cloud)} · ${c.componentName}): ${countWords(c)}`)
    const top = reasons?.groups[0]
    if (reasons && reasons.groups.length > 0) lines.push(`  - by reason: ${reasonWords(reasons)}`)
    else if (reasons === null && sizeOf(c) > 0) lines.push('  - reasons could not be read')
    const places = c.entities
      .filter((e) => (e.deadLetters.value ?? 0) > 0)
      .sort((a, b) => (b.deadLetters.value ?? 0) - (a.deadLetters.value ?? 0))
      .slice(0, 3)
    if (places.length > 0) lines.push(`  - most in: ${places.map((e) => `\`${e.parent ? `${e.parent}/` : ''}${e.name}\` (${e.deadLetters.value})`).join(', ')}`)
    if (c.approvals > 0) lines.push(`  - **${c.approvals}** wait in **Pending approvals**`)
    if (focus === 'replay' && !c.canSend) lines.push("  - this connection is **read-only**, so ServiceHub won't replay through it")

    if (sizeOf(c) > 0) {
      const tab = `/${enc(c.cloud)}/c/${enc(c.componentKey)}/${enc(c.namespaceId)}?tab=dead-letters`
      links.push(
        top
          ? { label: `Open the ${top.count} ${top.reason ?? 'with no reason'} on ${c.name}`, href: `${tab}&reason=${enc(top.reason ?? NO_REASON)}` }
          : { label: `Open ${c.name}'s dead-letter messages`, href: tab },
      )
      if (focus === 'replay' && top?.reason) {
        const tip = reasonTip(top.reason)
        if (tip) tips.add(`**${top.reason}** ${tip}`)
      }
    }
    if (c.approvals > 0 && !links.some((l) => l.href === `/${enc(c.cloud)}/approvals`)) {
      links.push({ label: `Open Pending approvals (${cloudName(c.cloud)})`, href: `/${enc(c.cloud)}/approvals` })
    }
  }
  if (ordered.length > shown.length) lines.push(`- …and ${ordered.length - shown.length} more connections`)
  if (stores.every((c) => sizeOf(c) === 0 && c.watched && c.reachable)) lines.push('', 'Nothing is in dead-letter on your connections right now.')
  if (tips.size > 0) lines.push('', ...[...tips].map((t) => `⚠️ ${t}`))
  return { markdown: lines.join('\n'), links }
}

/** One connection's count, in the words its kind allows. A count the cloud did not give is never shown as a number. */
function countWords(c: ConnectedComponent): string {
  if (!c.reachable) return `couldn't be read${c.lastError ? `: ${c.lastError}` : ''}`
  const parts: string[] = []
  const v = c.deadLetters.value
  switch (c.deadLetters.kind) {
    case 'exact':
      parts.push(`**${v ?? 0}** dead letters`)
      break
    case 'approximate':
      parts.push(`about **${v ?? 0}** dead letters (the cloud's own estimate)`)
      break
    case 'notReported':
      parts.push("the cloud doesn't report a count")
      break
    case 'notRead':
      parts.push('not read yet')
      break
  }
  if (!c.watched) {
    const r = c.recorded
    parts.push(
      r?.atUtc && r.messages !== null
        ? `**${r.messages}${r.reachedLimit ? '+' : ''}** recorded at the last **Look now** (${when(r.atUtc)})`
        : 'not watched automatically: choose **Look now** to record them',
    )
  }
  return parts.join('; ')
}

/** "547 MaxDeliveryCountExceeded, 27 DeserializationError, 3 more of 2 other kinds" */
function reasonWords(list: DeadLetterList): string {
  const shown = list.groups.slice(0, 3).map((g) => `${g.count} ${g.reason ?? 'with no reason recorded'}`)
  const rest = list.groups.slice(3).reduce((n, g) => n + g.count, 0) + (list.otherReasons?.count ?? 0)
  const restKinds = Math.max(0, list.groups.length - 3) + (list.otherReasons?.kinds ?? 0)
  if (rest > 0) shown.push(`${rest} more of ${restKinds} other ${restKinds === 1 ? 'kind' : 'kinds'}`)
  return shown.join(', ')
}

/** What to fix before replaying, for reasons whose meaning is certain. Unknown reasons get no tip rather than a guess. */
function reasonTip(reason: string): string | undefined {
  const r = reason.toLowerCase()
  if (r === 'maxdeliverycountexceeded') return 'means the consumer failed on every delivery. Fix the consumer first, or replayed messages will come straight back.'
  if (r === 'ttlexpiredexception') return 'means the message expired before anyone processed it. Once the consumer keeps up, a replay usually works.'
  if (/deserializ|serializ|parse|schema/.test(r)) return "means the message couldn't be read. Replaying the same body fails the same way until the consumer or the sender's format is fixed."
  return undefined
}

// ---- Replay results: the last 7 days, in the outcome words ----

/** The ledger's states in the 5.0.0 outcome words (`next/lib/words.ts`), and what is a replay outcome at all. */
const OUTCOME_WORDS: Readonly<Record<string, { word: string; replay: boolean }>> = {
  Observing: { word: 'Watching', replay: true },
  Recovered: { word: 'Verified', replay: true },
  Returned: { word: 'Came back', replay: true },
  Unverified: { word: 'Unverified', replay: true },
  ExecutionFailed: { word: 'Not sent', replay: true },
  ExecutionUnknown: { word: 'Outcome unknown', replay: true },
  Executing: { word: 'In flight', replay: true },
  Declined: { word: 'Declined by a person', replay: false },
  Discarded: { word: 'Purged', replay: false },
  Expired: { word: 'Expired', replay: false },
  WrittenOff: { word: 'Written off', replay: false },
}

async function replayResults(reader: ServiceHubReader): Promise<LiveResult> {
  const summary = await reader.get<RecoverySummary>('/recovery/summary', { window: SUMMARY_WINDOW })
  const links: Link[] = [{ label: 'Open Replay history', href: '/all/replays' }]
  if (summary.total === 0) return { markdown: '**Last 7 days** (live from your ServiceHub): no replays.', links }

  const lines = [`**Last 7 days** (live from your ServiceHub):`]
  const byCloud = summary.byProvider.length > 1 ? summary.byProvider : [{ provider: '', total: summary.total, states: summary.states, stayedFixedRate: summary.stayedFixedRate }]
  for (const p of byCloud) {
    const replays = stateWords(p.states, true)
    const other = stateWords(p.states, false)
    const prefix = p.provider ? `**${providerName(p.provider)}:** ` : ''
    lines.push(`- ${prefix}${replays || 'no replays'}${other ? `; also ${other}` : ''}`)
    if (p.stayedFixedRate !== null) lines.push(`  - **${Math.round(p.stayedFixedRate * 100)}%** stayed fixed (Verified out of Verified plus Came back; Unverified is on neither side)`)
  }
  return { markdown: lines.join('\n'), links }
}

function stateWords(states: readonly StateCount[], replay: boolean): string {
  return states
    .filter((s) => s.count > 0 && (OUTCOME_WORDS[s.state]?.replay ?? true) === replay)
    .map((s) => `${s.count} ${OUTCOME_WORDS[s.state]?.word ?? s.state}`)
    .join(', ')
}

// ---- Connections: what is connected and whether it answered ----

async function connections(reader: ServiceHubReader): Promise<LiveResult> {
  // Every connection comes from /namespaces; a connection with no component switched on has no row in /fleet/components.
  const [namespaces, overview, coverage] = await Promise.all([
    reader.get<readonly Connection[]>('/namespaces'),
    reader.get<ComponentOverview>('/fleet/components'),
    reader.get<Coverage>('/fleet/coverage'),
  ])
  if (namespaces.length === 0) return notConnected()
  const cloudName = cloudNames(coverage)

  const lines = ['**Right now** (live from your ServiceHub):']
  for (const n of namespaces.slice(0, MAX_CONNECTIONS * 2)) {
    const parts = overview.components.filter((c) => c.namespaceId === n.id)
    const failing = parts.find((p) => !p.reachable)
    const what = parts.length > 0 ? parts.map((p) => p.componentName).join(', ') : 'no component switched on'
    const test =
      n.lastConnectionTestAt === null
        ? 'never tested'
        : `last connection test ${n.lastConnectionTestSucceeded ? 'passed' : '**failed**'} (${when(n.lastConnectionTestAt)})`
    const read = failing ? `. ⚠️ Its last read failed${failing.lastError ? `: ${failing.lastError}` : ''}` : ''
    const flags = [n.environment, n.isActive ? '' : 'switched off', parts.length > 0 && parts.every((p) => !p.canSend) ? 'read-only' : '']
      .filter(Boolean)
      .join(', ')
    lines.push(`- **${n.displayName ?? n.name}** (${cloudName(n.provider)}, ${flags}): ${what}; ${test}${read}.`)
  }
  if (namespaces.length > MAX_CONNECTIONS * 2) lines.push(`- …and ${namespaces.length - MAX_CONNECTIONS * 2} more`)
  return { markdown: lines.join('\n'), links: [{ label: 'Open Connections', href: '/connections' }] }
}

// ---- Shared ----

function notConnected(): LiveResult {
  return {
    markdown: '**Right now:** no cloud is connected yet. Choose **Connect a cloud** to start, or try the sample data.',
    links: [{ label: 'Open Connections', href: '/connections' }],
  }
}

/** Cloud display names from ServiceHub's own coverage list; the id itself if it is not there. */
function cloudNames(coverage: Coverage): (cloud: string) => string {
  const names = new Map(coverage.rows.map((r) => [r.cloud, r.cloudName]))
  return (cloud) => names.get(cloud) ?? cloud
}

/** `/recovery/summary` names clouds by provider (`azure`, `aws`, `gcp`); same ids as `cloud`, title-cased if unknown. */
function providerName(provider: string): string {
  return ({ azure: 'Azure', aws: 'AWS', gcp: 'Google Cloud' } as Record<string, string>)[provider.toLowerCase()] ?? provider
}

function sizeOf(c: ConnectedComponent): number {
  return Math.max(c.deadLetters.value ?? 0, c.recorded?.messages ?? 0)
}

function when(iso: string): string {
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? iso : `${d.toISOString().slice(0, 16).replace('T', ' ')} UTC`
}

function enc(value: string): string {
  return encodeURIComponent(value)
}
