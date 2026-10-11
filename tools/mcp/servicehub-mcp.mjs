#!/usr/bin/env node
// ServiceHub MCP server — lets an AI assistant ASK ServiceHub about dead letters. Read-only.
//
//   SERVICEHUB_URL      where your ServiceHub answers (default http://localhost:8080)
//   SERVICEHUB_API_KEY  optional: an API key ServiceHub knows, sent as X-API-KEY
//
// It can look. It cannot act: there is no tool that replays, purges, approves or changes anything, and the code below can
// only ever send a GET to a fixed list of addresses. Acting stays with a person, or with ServiceHub's own safety checks.
//
// No dependencies: it speaks the Model Context Protocol (JSON-RPC 2.0, one JSON message per line) over stdin/stdout.

const PROTOCOL = '2024-11-05'
const SERVER = { name: 'servicehub', version: '0.1.0' }

const clouds = { type: 'string', enum: ['Azure', 'Aws', 'Gcp'], description: 'Limit to one cloud.' }
const id = { type: 'integer', minimum: 1, description: 'The dead letter\'s ID in ServiceHub (from list_dead_letters).' }

/** Every tool is one GET. `path` builds the address from the arguments; nothing else is ever requested. */
export const tools = [
  {
    name: 'list_clouds',
    description: 'The cloud namespaces connected to ServiceHub (Azure Service Bus, AWS SQS, Google Pub/Sub), and what each cloud can and cannot do. Credentials are never included.',
    inputSchema: { type: 'object', properties: {} },
    path: () => ['/namespaces', {}],
  },
  {
    name: 'list_dead_letters',
    description: 'Messages that failed and were set aside. Returns where each failed, why (when the cloud recorded it), how many times it was tried, and a count per failure reason.',
    inputSchema: {
      type: 'object',
      properties: {
        provider: clouds,
        namespaceId: { type: 'string', description: 'Limit to one namespace (an ID from list_clouds).' },
        status: { type: 'string', enum: ['active', 'resolved', 'all'], description: 'active = still stuck (default).' },
        reason: { type: 'string', description: 'Only this dead-letter reason.' },
        entity: { type: 'string', description: 'Only this queue or subscription.' },
        q: { type: 'string', description: 'Search in message ID, queue, reason and error text.' },
        page: { type: 'integer', minimum: 1 },
        pageSize: { type: 'integer', minimum: 1, maximum: 50 },
      },
    },
    path: (a) => ['/dead-letters', { provider: a.provider, namespaceId: a.namespaceId, status: a.status ?? 'active', reason: a.reason, entity: a.entity, q: a.q, page: a.page ?? 1, pageSize: Math.min(a.pageSize ?? 20, 50) }],
  },
  {
    name: 'get_dead_letter',
    description: 'One dead letter in full: the start of its body, its properties, its error, and how many others failed the same way. The body can contain business data — only ask when it is needed.',
    inputSchema: { type: 'object', properties: { id }, required: ['id'] },
    path: (a) => [`/dead-letters/${int(a.id)}`, {}],
  },
  {
    name: 'explain_replay',
    description: 'What WOULD happen if this dead letter were replayed: the safety checks and whether each passes, and whether the cloud can confirm the fix afterwards. This does not replay anything.',
    inputSchema: { type: 'object', properties: { id }, required: ['id'] },
    path: (a) => [`/dead-letters/${int(a.id)}/replay-proposal`, {}],
  },
  {
    name: 'list_failure_kinds',
    description: 'Kinds of failure seen recently, grouped: how many messages, whether it is growing, and whether replaying it has helped before.',
    inputSchema: { type: 'object', properties: { provider: clouds, days: { type: 'integer', minimum: 1, maximum: 90 }, onlyGrowing: { type: 'boolean' } } },
    path: (a) => ['/signatures', { provider: a.provider?.toLowerCase(), days: a.days ?? 7, tab: a.onlyGrowing ? 'growing' : 'all', sort: 'messages', page: 1, pageSize: 25 }],
  },
  {
    name: 'failure_trust',
    description: 'Whether ServiceHub may replay one kind of failure on its own, and why or why not: how many verified fixes it has, at what rate, and what it still needs.',
    inputSchema: { type: 'object', properties: { signatureHash: { type: 'string', description: 'From list_failure_kinds.' }, provider: clouds }, required: ['signatureHash', 'provider'] },
    path: (a) => [`/signatures/${encodeURIComponent(String(a.signatureHash))}/trust`, { provider: String(a.provider).toLowerCase() }],
  },
  {
    name: 'list_replays',
    description: 'Messages that were replayed and how each ended: stayed fixed, came back, still being watched, or could not be proven.',
    inputSchema: {
      type: 'object',
      properties: {
        provider: clouds,
        ending: { type: 'string', enum: ['fixed', 'watching', 'returned', 'unproven', 'notsent'] },
        window: { type: 'string', enum: ['24h', '7d', '30d', 'all'] },
        by: { type: 'string', enum: ['people', 'autonomous'], description: 'Replayed by a person, or by ServiceHub on its own.' },
        page: { type: 'integer', minimum: 1 },
      },
    },
    path: (a) => ['/replays', { provider: a.provider, ending: a.ending, window: a.window ?? '7d', by: a.by, page: a.page ?? 1, pageSize: 25 }],
  },
  {
    name: 'waiting_for_a_person',
    description: 'What ServiceHub has stopped and asked a person about: replays an agent may not make alone, and rules that stopped themselves.',
    inputSchema: { type: 'object', properties: { provider: clouds } },
    path: (a) => ['/pending-work', { provider: a.provider }],
  },
  {
    name: 'recovery_summary',
    description: 'Counts of replay outcomes over a period, per cloud, and the share that stayed fixed.',
    inputSchema: { type: 'object', properties: { provider: clouds, window: { type: 'string', enum: ['24h', '7d', '30d', 'all'] } } },
    path: (a) => ['/recovery/summary', { provider: a.provider, window: a.window ?? '7d' }],
  },
  {
    name: 'list_agents',
    description: 'ServiceHub\'s agents: what each one is for, what it may and may not do, whether it is running or paused, and what it last did.',
    inputSchema: { type: 'object', properties: {} },
    path: () => ['/agents', {}],
  },
]

function int(value) {
  const n = Number(value)
  if (!Number.isSafeInteger(n) || n < 1) throw new Error('That is not a dead letter ID.')
  return n
}

/** The address for one tool call. Throws for a tool that does not exist — there is no way to name an address directly. */
export function addressFor(base, name, args = {}) {
  const tool = tools.find((t) => t.name === name)
  if (!tool) throw new Error(`There is no tool called '${name}'.`)
  const [path, query] = tool.path(args ?? {})
  const url = new URL(`/api/v1${path}`, base)
  for (const [key, value] of Object.entries(query)) if (value !== undefined && value !== null && value !== '') url.searchParams.set(key, String(value))
  return url
}

export function createServer({ base = process.env.SERVICEHUB_URL ?? 'http://localhost:8080', apiKey = process.env.SERVICEHUB_API_KEY, fetchImpl = fetch } = {}) {
  async function call(name, args) {
    const url = addressFor(base, name, args)
    const headers = { Accept: 'application/json' }
    if (apiKey) headers['X-API-KEY'] = apiKey
    let response
    try {
      response = await fetchImpl(url, { method: 'GET', headers }) // GET only, always
    } catch {
      return { isError: true, content: [{ type: 'text', text: `ServiceHub could not be reached at ${url.origin}. Is it running, and is SERVICEHUB_URL right?` }] }
    }
    const text = await response.text()
    if (!response.ok) {
      let detail = text
      try { detail = JSON.parse(text).detail ?? text } catch { /* not JSON */ }
      return { isError: true, content: [{ type: 'text', text: `ServiceHub answered ${response.status}: ${String(detail).slice(0, 500)}` }] }
    }
    return { content: [{ type: 'text', text }] }
  }

  /** Handles one message. Returns the reply, or null for a notification. */
  return async function handle(message) {
    const { id: requestId, method, params } = message ?? {}
    const reply = (result) => (requestId === undefined ? null : { jsonrpc: '2.0', id: requestId, result })
    const fail = (code, text) => (requestId === undefined ? null : { jsonrpc: '2.0', id: requestId, error: { code, message: text } })
    try {
      switch (method) {
        case 'initialize':
          return reply({ protocolVersion: PROTOCOL, capabilities: { tools: {} }, serverInfo: SERVER, instructions: 'Read-only access to ServiceHub: dead letters, why they failed, and what happened when they were replayed. Nothing here can replay, purge or approve.' })
        case 'ping':
          return reply({})
        case 'tools/list':
          return reply({ tools: tools.map(({ name, description, inputSchema }) => ({ name, description, inputSchema, annotations: { readOnlyHint: true } })) })
        case 'tools/call':
          if (!tools.some((t) => t.name === params?.name)) return fail(-32602, `There is no tool called '${params?.name}'.`)
          try {
            return reply(await call(params.name, params.arguments))
          } catch (error) {
            return reply({ isError: true, content: [{ type: 'text', text: error instanceof Error ? error.message : 'That could not be asked.' }] })
          }
        default:
          return method?.startsWith('notifications/') ? null : fail(-32601, `'${method}' is not something this server does.`)
      }
    } catch (error) {
      return fail(-32603, error instanceof Error ? error.message : 'Something went wrong.')
    }
  }
}

// Run as a program: one JSON message per line on stdin, one per line on stdout. Nothing else is ever written to stdout.
if (process.argv[1] && import.meta.url === new URL(`file://${process.argv[1]}`).href) {
  const handle = createServer()
  let buffer = ''
  process.stdin.setEncoding('utf8')
  process.stdin.on('data', async (chunk) => {
    buffer += chunk
    let newline
    while ((newline = buffer.indexOf('\n')) >= 0) {
      const line = buffer.slice(0, newline).trim()
      buffer = buffer.slice(newline + 1)
      if (!line) continue
      let message
      try {
        message = JSON.parse(line)
      } catch {
        process.stdout.write(`${JSON.stringify({ jsonrpc: '2.0', id: null, error: { code: -32700, message: 'That was not JSON.' } })}\n`)
        continue
      }
      const answer = await handle(message)
      if (answer) process.stdout.write(`${JSON.stringify(answer)}\n`)
    }
  })
}
