import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { httpReader, LiveReadError, READ_PATHS } from '../src/servicehub.ts'
import type { ReadPath } from '../src/servicehub.ts'

interface Seen { url: string; init: RequestInit }

function fakeFetch(respond: (url: string) => Response | Promise<Response>): { fetch: typeof fetch; seen: Seen[] } {
  const seen: Seen[] = []
  const fn = (async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input)
    seen.push({ url, init: init ?? {} })
    return respond(url)
  }) as typeof fetch
  return { fetch: fn, seen }
}

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })

describe('httpReader', () => {
  it('sends GET only, to /api/v1 plus the path, with the query', async () => {
    const f = fakeFetch(() => json({ ok: true }))
    const reader = httpReader({ baseUrl: 'http://localhost:5153', fetch: f.fetch })
    assert.deepEqual(await reader.get('/dead-letters', { namespaceId: 'abc', pageSize: 1, skipped: undefined }), { ok: true })
    assert.equal(f.seen[0]?.url, 'http://localhost:5153/api/v1/dead-letters?namespaceId=abc&pageSize=1')
    assert.equal(f.seen[0]?.init.method, 'GET')
    assert.equal(f.seen[0]?.init.redirect, 'error')
  })

  it('refuses a path that is not on the list, before any request', async () => {
    const f = fakeFetch(() => json({}))
    const reader = httpReader({ baseUrl: 'http://localhost:5153', fetch: f.fetch })
    await assert.rejects(reader.get('/dead-letters/1/replay' as ReadPath), LiveReadError)
    assert.equal(f.seen.length, 0)
  })

  it('reads only endpoints the app itself reads, none of which changes anything', () => {
    assert.deepEqual([...READ_PATHS].sort(), ['/dead-letters', '/fleet/components', '/fleet/coverage', '/namespaces', '/recovery/summary'])
  })

  it('passes on only the person\'s own auth headers', async () => {
    const f = fakeFetch(() => json({}))
    const reader = httpReader({
      baseUrl: 'http://localhost:5153',
      fetch: f.fetch,
      headers: { authorization: 'Bearer t', cookie: 'a=b', 'x-api-key': 'k', 'x-servicehub-session': 's', 'x-ms-client-principal': 'forged', 'x-forwarded-for': '1.2.3.4', host: 'evil' },
    })
    await reader.get('/namespaces')
    assert.deepEqual(f.seen[0]?.init.headers, { accept: 'application/json', authorization: 'Bearer t', cookie: 'a=b', 'x-api-key': 'k', 'x-servicehub-session': 's' })
  })

  it('turns failures into sentences a person can act on', async () => {
    const cases: [Response | Error, RegExp][] = [
      [json({}, 403), /aren't allowed to see this/],
      [json({}, 401), /aren't allowed to see this/],
      [json({ title: 'Bad', detail: "Say which dead letters: give a 'namespaceId'." }, 400), /ServiceHub said: Say which dead letters/],
      [new Response('oops', { status: 502 }), /ServiceHub answered 502/],
      [new Response('not json', { status: 200 }), /could not read/],
      [new TypeError('fetch failed'), /could not be reached/],
    ]
    for (const [outcome, expected] of cases) {
      const reader = httpReader({ baseUrl: 'http://x', fetch: fakeFetch(() => { if (outcome instanceof Error) throw outcome; return outcome }).fetch })
      await assert.rejects(reader.get('/namespaces'), expected)
    }
  })

  it('gives up after the timeout and says so', async () => {
    const never = fakeFetch(() => new Promise<Response>(() => {}))
    const slow = (async (url: string | URL | Request, init?: RequestInit) => {
      const signal = init?.signal
      return new Promise<Response>((_, reject) => signal?.addEventListener('abort', () => reject(signal.reason)))
    }) as typeof fetch
    void never
    const reader = httpReader({ baseUrl: 'http://x', fetch: slow, timeoutMs: 50 })
    await assert.rejects(reader.get('/namespaces'), /did not answer within 0 seconds|did not answer within/)
  })
})
