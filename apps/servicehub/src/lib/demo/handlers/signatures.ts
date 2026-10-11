import type { AuthoritySpread, IncidentStory, SignaturePage, TraceResult } from '../../api/signatures'
import { L4, lowerProvider, nsOf, proves, scopeOf, signaturesOf, toTrust, trustOf } from '../world/derive'
import { notFound, type Route } from './http'

const cloudName = { azure: 'Azure', aws: 'Aws', gcp: 'Gcp' } as const

export const signatures: readonly Route[] = [
  ['get', /^\/signatures$/, (_m, { w, params: p, now }): SignaturePage => {
    const days = Number(p.days ?? 7)
    const q = p.q ? String(p.q).toLowerCase() : null
    const all = signaturesOf(w, scopeOf(p), days, now).filter((s) => (!p.entity || s.entities.includes(String(p.entity))) && (!q || `${s.reason} ${s.exampleError ?? ''} ${s.entities.join(' ')}`.toLowerCase().includes(q)))
    const tab = String(p.tab ?? 'all')
    const shown = all
      .filter((s) => tab === 'all' || (tab === 'growing' ? s.growing : tab === 'helps' ? s.replayVerdict === 'helps' : s.replayVerdict === 'doesnt'))
      .sort((a, b) => (p.sort === 'recent' ? b.lastSeenAt.localeCompare(a.lastSeenAt) : b.messages - a.messages))
    const number = Math.max(1, Number(p.page ?? 1))
    const size = Math.max(1, Number(p.pageSize ?? 25))
    return {
      items: shown.slice((number - 1) * size, number * size), total: shown.length, page: number, pageSize: size,
      all: all.length, growing: all.filter((s) => s.growing).length, replayHelps: all.filter((s) => s.replayVerdict === 'helps').length, replayDoesNotHelp: all.filter((s) => s.replayVerdict === 'doesnt').length,
    }
  }],
  ['get', /^\/signatures\/authority$/, (_m, { w, params, now }): AuthoritySpread => {
    const list = signaturesOf(w, { provider: lowerProvider(params.provider) }, Number(params.days ?? 7), now)
    const held = new Map<AuthoritySpread['held'][number]['reason'], number>()
    let unattended = 0, standing = 0, approve = 0
    for (const s of list) {
      const t = trustOf(w, s.signatureHash, s.provider)
      if (t.level === 'unattended') unattended++
      else if (t.level === 'standing') standing++
      else {
        approve++
        const why = !proves(s.provider) ? 'cannot_verify' : t.sample < L4.sample ? 'needs_evidence' : 'rate_too_low'
        held.set(why, (held.get(why) ?? 0) + 1)
      }
    }
    return { total: list.length, capped: false, unattended, standing, approve, held: [...held].map(([reason, count]) => ({ reason, count })), needs: { sample: L4.sample, rate: L4.rate } }
  }],
  ['get', /^\/signatures\/([^/]+)\/trust$/, (m, { w, params }) => {
    const hash = decodeURIComponent(m[1])
    const row = w.deadLetters.find((d) => d.signatureHash === hash) ?? notFound('kind of failure')
    return toTrust(w, hash, lowerProvider(params.provider) ?? nsOf(w, row.namespaceId).provider)
  }],
  ['get', /^\/signatures\/([^/]+)\/incident$/, (m, { w }): IncidentStory => {
    const hash = decodeURIComponent(m[1])
    const rows = w.deadLetters.filter((d) => d.signatureHash === hash)
    if (rows.length === 0) notFound('kind of failure')
    const provider = nsOf(w, rows[0].namespaceId).provider
    const entries = w.entries.filter((e) => e.signatureHash === hash)
    const seen = rows.map((d) => d.detectedAtUtc).sort()
    const timeline: IncidentStory['timeline'][number][] = [{ at: seen[0], kind: 'first_seen', text: `First seen on ${rows[0].entityName}`, entryId: null, dlqMessageId: rows.find((d) => d.detectedAtUtc === seen[0])!.id }]
    for (const e of entries) {
      timeline.push({ at: e.begunAt, kind: e.kind === 'Purge' ? 'purged' : 'replayed', text: `${e.kind === 'Purge' ? 'Purged' : e.kind === 'WriteOff' ? 'Written off' : 'Replayed'} by ${e.actor.label}`, entryId: e.id, dlqMessageId: e.dlqMessageId })
      if (e.state === 'Returned' && e.closedAt) timeline.push({ at: e.closedAt, kind: 'came_back', text: 'Came back to the dead-letter queue', entryId: e.id, dlqMessageId: e.dlqMessageId })
    }
    timeline.sort((a, b) => b.at.localeCompare(a.at))
    const replays = entries.filter((e) => e.kind === 'Replay')
    return {
      signatureHash: hash, provider, reason: rows[0].deadLetterReason, firstSeenAt: seen[0], lastSeenAt: seen[seen.length - 1], messages: rows.length, stillStuck: rows.filter((d) => d.status === 'active').length,
      replays: replays.length, stayedFixed: replays.filter((e) => e.state === 'Recovered').length, cameBackAfterReplay: replays.filter((e) => e.state === 'Returned').length, timeline: timeline.slice(0, 60),
    }
  }],
  ['get', /^\/trace$/, (_m, { w, params }): TraceResult => {
    const correlationId = String(params.correlationId ?? '').trim()
    const rows = w.deadLetters.filter((d) => d.correlationId === correlationId)
    const hops: TraceResult['hops'][number][] = []
    for (const d of rows) {
      const ns = nsOf(w, d.namespaceId)
      const place = { provider: cloudName[ns.provider], namespaceName: ns.displayName ?? ns.name, environment: ns.environment }
      hops.push({ at: d.detectedAtUtc, kind: 'dead_lettered', place, entity: d.entityName, dlqMessageId: d.id, namespaceId: ns.id, detail: d.deadLetterReason, entryId: null, messageId: d.messageId })
      for (const e of w.entries.filter((x) => x.dlqMessageId === d.id)) hops.push({ at: e.begunAt, kind: e.kind === 'Purge' ? 'purged' : 'replayed', place, entity: e.entityName, dlqMessageId: d.id, namespaceId: ns.id, detail: `by ${e.actor.label}`, entryId: e.id, messageId: d.messageId })
    }
    hops.sort((a, b) => a.at.localeCompare(b.at))
    return { correlationId, clouds: [...new Set(hops.map((h) => h.place.provider))], hops, note: 'A trace shows only what ServiceHub recorded: dead letters, and what was done about them.' }
  }],
]
