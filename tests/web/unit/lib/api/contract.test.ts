import type { AxiosRequestConfig, AxiosResponse } from 'axios'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { api } from '@/lib/api/client'
import { INTENT_HEADER } from '@/lib/api/intentHeaders'
import * as agents from '@/lib/api/agents'
import * as backup from '@/lib/api/backup'
import * as insights from '@/lib/api/insights'
import * as namespaces from '@/lib/api/namespaces'
import * as pending from '@/lib/api/pendingWork'
import * as recovery from '@/lib/api/recovery'
import * as replay from '@/lib/api/replay'
import * as rules from '@/lib/api/rules'
import * as settings from '@/lib/api/settings'
import * as signatures from '@/lib/api/signatures'

/**
 * The wire contract of every API client module, checked against the real axios instance with a recording adapter in place of the
 * network: which verb, which path, which query, and — for anything that changes state — that the intent header rides along (the API
 * refuses a mutating request without it). A client that drifts from the controller fails here, before it fails on a live cloud.
 */
type Seen = { method: string; url: string; params?: unknown; data?: unknown; intent?: string }
let seen: Seen[] = []
let original: typeof api.defaults.adapter

beforeEach(() => {
  seen = []
  original = api.defaults.adapter
  api.defaults.adapter = async (config: AxiosRequestConfig): Promise<AxiosResponse> => {
    seen.push({
      method: String(config.method).toUpperCase(),
      url: String(config.url),
      params: config.params,
      data: typeof config.data === 'string' ? JSON.parse(config.data) : config.data,
      intent: (config.headers as unknown as { get(name: string): string | undefined }).get(INTENT_HEADER),
    })
    return { data: config.responseType === 'blob' ? new Blob(['x']) : {}, status: 200, statusText: 'OK', headers: {}, config: config as never }
  }
  URL.createObjectURL = () => 'blob:test'
  URL.revokeObjectURL = () => undefined
  // jsdom cannot navigate to a blob: URL; the download link's click is all these tests need to observe.
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined)
})
afterEach(() => { api.defaults.adapter = original; vi.restoreAllMocks() })

const last = () => seen[seen.length - 1]!

describe('reads', () => {
  const reads: [string, () => Promise<unknown>, string, unknown?][] = [
    ['rules', () => rules.fetchRules('azure'), '/rules', { provider: 'azure' }],
    ['rules held', () => rules.fetchRulesHeld('aws'), '/rules/held', { provider: 'aws' }],
    ['rule sources', () => rules.fetchRuleSources('gcp'), '/rules/sources', { provider: 'gcp' }],
    ['rule matches', () => rules.fetchRuleMatches(7), '/rules/7/matches', { limit: 500 }],
    ['settings', () => settings.fetchSettings(), '/settings'],
    ['emergency stop', () => settings.fetchEmergencyStop(), '/settings/emergency-stop'],
    ['grants', () => settings.fetchGrants(), '/governance/grants'],
    ['backups', () => backup.fetchBackups(), '/admin/backup'],
    ['backup check', () => backup.checkBackup('a/b'), '/admin/backup/a%2Fb/check'],
    ['signature trust', () => signatures.fetchSignatureTrust('h#1', 'azure'), '/signatures/h%231/trust', { provider: 'azure' }],
    ['incident', () => signatures.fetchIncident('h', 'aws'), '/signatures/h/incident', { provider: 'Aws' }],
    ['trace', () => signatures.fetchTrace('c-1'), '/trace', { correlationId: 'c-1' }],
    ['agents', () => agents.fetchAgents(), '/agents'],
    ['fix confirmation', () => namespaces.fetchDlqObserver('n1'), '/namespaces/n1/dlq-observer'],
    ['agent activity', () => agents.fetchAgentActivity('a b'), '/agents/a%20b/activity'],
    ['ledger entry', () => recovery.fetchLedgerEntry('e1'), '/recovery/entries/e1'],
    ['chain', () => recovery.verifyChain(), '/recovery/chain'],
    ['replay proposal', () => replay.fetchReplayProposal(12), '/dead-letters/12/replay-proposal'],
  ]
  it.each(reads)('%s → GET', async (_name, call, url, params) => {
    await call()
    expect(last()).toMatchObject({ method: 'GET', url })
    if (params) expect(last().params).toEqual(params)
    expect(last().intent).toBeUndefined()
  })

  it('sends the API’s PascalCase provider names where the controller expects them', async () => {
    await signatures.fetchAuthority({ provider: 'gcp', days: 30 })
    expect(last()).toMatchObject({ url: '/signatures/authority', params: { provider: 'Gcp', days: 30 } })
    await insights.fetchInsights({ provider: 'aws', cleared: true })
    expect(last()).toMatchObject({ url: '/insights', params: { provider: 'Aws', cleared: true } })
    await insights.fetchInsights({})
    expect(last().params).toEqual({ provider: undefined, cleared: undefined })
    await pending.fetchPendingWork({ provider: 'azure' })
    expect(last()).toMatchObject({ url: '/pending-work', params: { provider: 'Azure' } })
    await replay.fetchReplays({ provider: 'gcp', window: '7d', page: 2 })
    expect(last()).toMatchObject({ url: '/replays', params: { provider: 'Gcp', window: '7d', page: 2 } })
  })

  it('passes the signatures query straight through', async () => {
    await signatures.fetchSignatures({ days: 7, tab: 'active' as never, sort: 'messages', page: 1, pageSize: 25, q: 'timeout' })
    expect(last()).toMatchObject({ method: 'GET', url: '/signatures', params: { days: 7, sort: 'messages', q: 'timeout' } })
  })

  it('lists ledger entries with only the filters given', async () => {
    await recovery.fetchLedger({ page: 3, pageSize: 10, by: 'autonomous', q: 'orders' })
    expect(last()).toMatchObject({ method: 'GET', url: '/recovery/entries' })
    expect(last().params).toMatchObject({ page: 3, pageSize: 10, by: 'autonomous', q: 'orders' })
  })
})

describe('changes carry the intent header the API demands', () => {
  const changes: [string, () => Promise<unknown>, string, string][] = [
    ['add channel', () => settings.addChannel({ format: 'slack' as never, label: 'ops', url: 'https://x' }), 'POST', '/settings/channels'],
    ['remove channel', () => settings.removeChannel('c1'), 'DELETE', '/settings/channels/c1'],
    ['emergency stop', () => settings.setEmergencyStop({ active: true, reason: 'why' }), 'POST', '/settings/emergency-stop'],
    ['grant', () => settings.grantRole({ granteeIdentity: 'a', granteeKind: 'User', role: 'Operator' as never, namespaceId: null }), 'POST', '/governance/grants'],
    ['revoke', () => settings.revokeRole('g1'), 'POST', '/governance/grants/g1/revoke'],
    ['create backup', () => backup.createBackup(), 'POST', '/admin/backup'],
    ['restore', () => backup.restoreBackup('b1', 'RESTORE'), 'POST', '/admin/backup/b1/restore'],
    ['cancel restore', () => backup.cancelRestore(), 'DELETE', '/admin/backup/pending'],
    ['pause agent', () => agents.pauseAgent('x'), 'POST', '/agents/x/pause'],
    ['resume agent', () => agents.resumeAgent('x'), 'POST', '/agents/x/resume'],
    ['approve', () => pending.approvePending('p1'), 'POST', '/pending-work/p1/approve'],
    ['decline', () => pending.declinePending('p1', 'no'), 'POST', '/pending-work/p1/decline'],
    ['replay', () => replay.replayMessage(5), 'POST', '/dead-letters/5/replay'],
    ['purge', () => replay.purgeMessage(5, 'dup'), 'POST', '/dead-letters/5/purge'],
    ['switch on fix confirmation', () => namespaces.configureDlqObserver('n1', { enabled: true }), 'PUT', '/namespaces/n1/dlq-observer'],
    ['check fix confirmation', () => namespaces.checkDlqObserver('n1'), 'POST', '/namespaces/n1/dlq-observer/check'],
  ]
  it.each(changes)('%s → %s with an intent', async (_name, call, method, url) => {
    await call()
    expect(last()).toMatchObject({ method, url })
    expect(last().intent).toBeTruthy()
  })

  it('records the reason and the typed confirmation in the body', async () => {
    await settings.setEmergencyStop({ active: false, confirm: 'LIFT' })
    expect(last().data).toEqual({ active: false, confirm: 'LIFT' })
    await pending.declinePending('p1', 'not this one')
    expect(last().data).toEqual({ reason: 'not this one' })
    await replay.purgeMessage(9, 'poison')
    expect(last().data).toEqual({ reason: 'poison' })
    await backup.restoreBackup('b1', 'RESTORE b1')
    expect(last().data).toEqual({ confirm: 'RESTORE b1' })
  })

  it('always attaches this browser session to the request', async () => {
    let header: unknown
    api.defaults.adapter = async (config) => { header = (config.headers as unknown as { get(n: string): unknown }).get('X-ServiceHub-Session'); return { data: {}, status: 200, statusText: '', headers: {}, config: config as never } }
    await agents.fetchAgents()
    expect(typeof header).toBe('string')
  })
})

describe('rules', () => {
  it('creates, toggles, tests, edits, deletes and generates over the right routes', async () => {
    await rules.createRule({ provider: 'azure', name: 'r' } as never)
    expect(last()).toMatchObject({ method: 'POST', url: '/rules' })
    await rules.setRuleEnabled(3, false)
    expect(last()).toMatchObject({ method: 'POST', url: '/rules/3/enabled', data: { enabled: false } })
    await rules.testRule({ provider: 'aws', reason: 'x', entityName: 'q', signatureHash: null } as never)
    expect(last()).toMatchObject({ method: 'POST', url: '/rules/test', data: { days: 7, provider: 'aws' } })
    await rules.updateRule(3, { name: 'n', maxPerHour: 5, waitSeconds: 60, backOff: true })
    expect(last()).toMatchObject({ method: 'PUT', url: '/rules/3' })
    await rules.deleteRule(3)
    expect(last()).toMatchObject({ method: 'DELETE', url: '/rules/3' })
    await rules.generateRules('gcp')
    expect(last()).toMatchObject({ method: 'POST', url: '/rules/generate', data: { provider: 'gcp', max: 5 } })
  })
})

describe('settings channels', () => {
  it('toggles and tests a channel without an intent (reversible, read-only in effect)', async () => {
    await settings.setChannelEnabled('c1', true)
    expect(last()).toMatchObject({ method: 'POST', url: '/settings/channels/c1/enabled', data: { enabled: true } })
    await settings.testChannel('c1')
    expect(last()).toMatchObject({ method: 'POST', url: '/settings/channels/c1/test' })
  })
})

describe('file downloads', () => {
  it('saves a backup under a name that carries its id', async () => {
    const clicked: string[] = []
    const real = document.createElement.bind(document)
    document.createElement = ((tag: string) => { const el = real(tag); if (tag === 'a') el.click = () => clicked.push((el as HTMLAnchorElement).download); return el }) as typeof document.createElement
    try {
      await backup.downloadBackup('b9')
      expect(last()).toMatchObject({ method: 'GET', url: '/admin/backup/b9/download' })
      expect(clicked).toEqual(['servicehub-backup-b9.db'])
    } finally { document.createElement = real }
  })

  it('exports evidence for a window, unbounded for "all", and never narrowed by cloud', async () => {
    const now = new Date('2026-09-29T12:00:00Z')
    await recovery.exportEvidence('24h', now)
    expect(last().params).toEqual({ from: '2026-09-28T12:00:00.000Z' })
    await recovery.exportEvidence('all', now)
    expect(last().params).toEqual({ from: undefined })
    expect(last().url).toBe('/recovery/export')
  })
})

describe('agent wording', () => {
  it('says cadence in words', () => {
    expect(agents.cadenceWords(30)).toBe('every 30 seconds')
    expect(agents.cadenceWords(60)).toBe('every minute')
    expect(agents.cadenceWords(3600)).toBe('every hour')
  })
})
