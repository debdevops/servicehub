import assert from 'node:assert/strict'
import type { AddressInfo } from 'node:net'
import type { Server } from 'node:http'
import { after, before, describe, it } from 'node:test'
import { createBotServer } from '../src/server.ts'
import type { KnowledgeBase } from '../src/kb.ts'
import { fakeReader } from './fixtures.ts'
import type { FakeReader } from './fixtures.ts'
import { openFreshKb } from './helpers.ts'

describe('bot server', () => {
  let kb: KnowledgeBase
  let server: Server
  let base: string
  const logs: string[] = []
  const readers: { headers: Record<string, unknown>; reader: FakeReader }[] = []

  before(async () => {
    kb = openFreshKb()
    server = createBotServer({
      kb,
      apiUrl: 'http://unused.invalid',
      tryPage: true,
      widgetSource: new URL('../widget/servicehub-bot.ts', import.meta.url).pathname,
      perMinute: 1000,
      log: (line) => logs.push(line),
      readerFor: (headers) => {
        const reader = fakeReader()
        readers.push({ headers: { ...headers }, reader })
        return reader
      },
    })
    await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve))
    base = `http://127.0.0.1:${(server.address() as AddressInfo).port}`
  })
  after(() => {
    server.close()
    kb.close()
  })

  const askBot = (body: unknown, headers: Record<string, string> = {}) =>
    fetch(`${base}/bot/ask`, { method: 'POST', headers: { 'content-type': 'application/json', ...headers }, body: typeof body === 'string' ? body : JSON.stringify(body) })

  it('answers a question, with live numbers read as the person asking', async () => {
    const res = await askBot({ question: 'how do I replay dlq' }, { 'x-api-key': 'k1', 'x-servicehub-session': 's1' })
    assert.equal(res.status, 200)
    const answer = (await res.json()) as { status: string; markdown: string; live?: { ok: boolean } }
    assert.equal(answer.status, 'answered')
    assert.equal(answer.live?.ok, true)
    assert.match(answer.markdown, /\*\*Shop\*\*/)
    const last = readers.at(-1)
    assert.equal(last?.headers['x-api-key'], 'k1')
    assert.equal(last?.headers['x-servicehub-session'], 's1')
  })

  it('reads nothing live when asked not to (the demo)', async () => {
    const before = readers.length
    const answer = (await (await askBot({ question: 'how do I replay dlq', live: false })).json()) as { live?: unknown; markdown: string }
    assert.equal(answer.live, undefined)
    assert.doesNotMatch(answer.markdown, /Right now/)
    assert.equal(readers.length, before)
  })

  it('reads nothing live for a question that needs no numbers', async () => {
    await askBot({ question: 'rotate encryption key' })
    assert.equal(readers.at(-1)?.reader.calls.length ?? 0, 0)
  })

  it('refuses what is not a JSON question, with a sentence', async () => {
    const cases: [Promise<Response>, number][] = [
      [fetch(`${base}/bot/ask`, { method: 'POST', headers: { 'content-type': 'text/plain' }, body: '{"question":"x"}' }), 415],
      [askBot('not json'), 400],
      [askBot({}), 400],
      [askBot({ question: '   ' }), 400],
      [askBot({ question: 'x', live: 'yes' }), 400],
      [askBot({ question: 'x'.repeat(501) }), 400],
      [askBot({ question: 'x'.repeat(5000) }), 413],
      [fetch(`${base}/bot/ask`), 405],
      [fetch(`${base}/bot/nope`), 404],
      [fetch(`${base}/api/v1/namespaces`), 404],
    ]
    for (const [pending, status] of cases) {
      const res = await pending
      assert.equal(res.status, status)
      assert.equal(typeof ((await res.json()) as { error?: unknown }).error, 'string')
    }
  })

  it('sends safe headers on every response', async () => {
    const res = await fetch(`${base}/bot/health`)
    assert.equal(res.headers.get('cache-control'), 'no-store')
    assert.equal(res.headers.get('x-content-type-options'), 'nosniff')
    assert.equal(res.headers.get('x-frame-options'), 'DENY')
    assert.equal(res.headers.get('access-control-allow-origin'), null, 'no CORS: other sites cannot read answers')
    assert.match(res.headers.get('content-security-policy') ?? '', /default-src 'none'/)
  })

  it('says which knowledge base it serves', async () => {
    const health = (await (await fetch(`${base}/bot/health`)).json()) as { ok: boolean; knowledgeBase: Record<string, string> }
    assert.equal(health.ok, true)
    assert.match(health.knowledgeBase.built_at ?? '', /^\d{4}-/)
  })

  it('serves the widget as plain JavaScript stripped from its TypeScript source, and it loads', async () => {
    const res = await fetch(`${base}/bot/widget.js`)
    assert.equal(res.status, 200)
    assert.match(res.headers.get('content-type') ?? '', /^text\/javascript/)
    const js = await res.text()
    assert.match(js, /customElements\.define\('servicehub-bot'/)
    assert.doesNotMatch(js, /#panel!: HTMLElement|: BotAnswer\)|export interface/)
    // It parses and runs as a module (in Node it defines nothing: there is no customElements here).
    const { mkdtempSync, writeFileSync } = await import('node:fs')
    const { join } = await import('node:path')
    const { tmpdir } = await import('node:os')
    const file = join(mkdtempSync(join(tmpdir(), 'servicehub-bot-widget-')), 'widget.mjs')
    writeFileSync(file, js)
    const mod = (await import(file)) as { safeHref: (h: string, c: string | null) => unknown }
    assert.deepEqual(mod.safeHref('/all/replays', 'aws'), { href: '/aws/replays', external: false })
  })

  it('serves the try-out page only when asked to', async () => {
    const res = await fetch(`${base}/bot/`)
    assert.equal(res.status, 200)
    assert.match(await res.text(), /<servicehub-bot><\/servicehub-bot>/)
  })

  it('never logs the question', async () => {
    await askBot({ question: 'my secret question about orders' })
    assert.ok(logs.length > 0)
    assert.ok(!logs.some((l) => l.includes('secret')), logs.join('\n'))
  })
})

describe('bot server rate limit', () => {
  it('slows a runaway client down, with a sentence and Retry-After', async () => {
    const kb = openFreshKb()
    let t = 0
    const server = createBotServer({ kb, apiUrl: 'http://unused.invalid', perMinute: 2, now: () => t, log: () => {}, readerFor: () => fakeReader() })
    await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve))
    const url = `http://127.0.0.1:${(server.address() as AddressInfo).port}/bot/ask`
    const askOnce = () => fetch(url, { method: 'POST', headers: { 'content-type': 'application/json' }, body: '{"question":"replay","live":false}' })
    try {
      assert.equal((await askOnce()).status, 200)
      assert.equal((await askOnce()).status, 200)
      const limited = await askOnce()
      assert.equal(limited.status, 429)
      assert.equal(limited.headers.get('retry-after'), '60')
      t = 60_000
      assert.equal((await askOnce()).status, 200, 'a new minute starts again')
    } finally {
      server.close()
      kb.close()
    }
  })
})
