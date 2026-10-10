import assert from 'node:assert/strict'
import { spawn } from 'node:child_process'
import { test } from 'node:test'
import { addressFor, createServer, tools } from './servicehub-mcp.mjs'

const BASE = 'http://servicehub.test:8080'

function fake(answers = {}) {
  const calls = []
  const fetchImpl = async (url, init) => {
    calls.push({ url: String(url), ...init })
    const body = answers[new URL(url).pathname] ?? { ok: true }
    return { ok: !body.__status, status: body.__status ?? 200, text: async () => JSON.stringify(body) }
  }
  return { calls, handle: createServer({ base: BASE, apiKey: 'k-1', fetchImpl }) }
}

const rpc = (method, params, id = 1) => ({ jsonrpc: '2.0', id, method, params })

test('it introduces itself and lists only read-only tools', async () => {
  const { handle } = fake()
  const hello = await handle(rpc('initialize', { protocolVersion: '2024-11-05', capabilities: {}, clientInfo: { name: 't', version: '1' } }))
  assert.equal(hello.result.serverInfo.name, 'servicehub')
  assert.deepEqual(hello.result.capabilities, { tools: {} })
  assert.equal(await handle({ jsonrpc: '2.0', method: 'notifications/initialized' }), null, 'a notification gets no reply')

  const list = await handle(rpc('tools/list'))
  assert.ok(list.result.tools.length >= 8)
  for (const tool of list.result.tools) {
    assert.equal(tool.annotations.readOnlyHint, true)
    assert.doesNotMatch(tool.name, /^(replay|purge|approve|decline|delete|create|update|send|pause|resume|set)_/, `${tool.name} sounds like it acts`)
  }
})

test('every tool sends one GET to a fixed address, with the key, and nothing else', async () => {
  const { handle, calls } = fake()
  const args = { id: 7, signatureHash: 'abc/../x', provider: 'Aws' }
  for (const tool of tools) await handle(rpc('tools/call', { name: tool.name, arguments: args }))

  assert.equal(calls.length, tools.length)
  for (const call of calls) {
    assert.equal(call.method, 'GET')
    assert.equal(call.headers['X-API-KEY'], 'k-1')
    assert.equal(call.body, undefined)
    const url = new URL(call.url)
    assert.equal(url.origin, BASE)
    assert.match(url.pathname, /^\/api\/v1\/(namespaces|dead-letters(\/7(\/replay-proposal)?)?|signatures(\/[^/]+\/trust)?|replays|pending-work|recovery\/summary|agents)$/)
  }
})

test('an argument can never steer it to another address', () => {
  assert.equal(addressFor(BASE, 'failure_trust', { signatureHash: '../../settings/emergency-stop', provider: 'Azure' }).pathname, '/api/v1/signatures/..%2F..%2Fsettings%2Femergency-stop/trust')
  assert.throws(() => addressFor(BASE, 'get_dead_letter', { id: '7/replay' }), /not a dead letter ID/)
  assert.throws(() => addressFor(BASE, 'get_dead_letter', { id: -1 }))
  assert.throws(() => addressFor(BASE, 'replay_message', { id: 7 }), /no tool/)
  assert.equal(addressFor(BASE, 'list_dead_letters', { pageSize: 5000 }).searchParams.get('pageSize'), '50')
})

test('it passes ServiceHub\'s answer back, and says plainly when ServiceHub refuses or is unreachable', async () => {
  const { handle } = fake({ '/api/v1/agents': [{ id: 'auto-replay' }], '/api/v1/pending-work': { __status: 403, detail: 'You have the Viewer role.' } })
  const ok = await handle(rpc('tools/call', { name: 'list_agents', arguments: {} }))
  assert.deepEqual(JSON.parse(ok.result.content[0].text), [{ id: 'auto-replay' }])

  const refused = await handle(rpc('tools/call', { name: 'waiting_for_a_person', arguments: {} }))
  assert.equal(refused.result.isError, true)
  assert.match(refused.result.content[0].text, /403: You have the Viewer role/)

  const down = createServer({ base: BASE, fetchImpl: async () => { throw new Error('ECONNREFUSED') } })
  const unreachable = await down(rpc('tools/call', { name: 'list_clouds', arguments: {} }))
  assert.match(unreachable.result.content[0].text, /could not be reached/)

  assert.equal((await handle(rpc('tools/call', { name: 'replay_message', arguments: { id: 1 } }))).error.code, -32602)
  assert.equal((await handle(rpc('nonsense'))).error.code, -32601)
})

test('as a program it speaks one JSON message per line and keeps stdout clean', async () => {
  const child = spawn(process.execPath, [new URL('./servicehub-mcp.mjs', import.meta.url).pathname], { env: { ...process.env, SERVICEHUB_URL: 'http://127.0.0.1:9' } })
  let out = ''
  child.stdout.on('data', (d) => { out += d })
  child.stdin.write(`${JSON.stringify(rpc('initialize', {}))}\nnot json\n${JSON.stringify(rpc('tools/list', undefined, 2))}\n`)
  await new Promise((resolve) => setTimeout(resolve, 400))
  child.kill()
  const lines = out.trim().split('\n').map((l) => JSON.parse(l))
  assert.equal(lines.length, 3)
  assert.equal(lines[0].result.protocolVersion, '2024-11-05')
  assert.equal(lines[1].error.code, -32700)
  assert.equal(lines[2].id, 2)
})
