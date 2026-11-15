/**
 * The bot as a small HTTP service: its own process, reached through ServiceHub's same-origin proxy so the browser's own
 * session is what gets passed on to ServiceHub. Everything is under `/bot/`, so the proxy forwards one prefix.
 *
 *   POST /bot/ask        { "question": "...", "live": true }  → Answer (see src/types.ts)
 *   GET  /bot/health     → { ok, knowledgeBase: { built_at, servicehub_commit, … } }
 *   GET  /bot/widget.js  → the floating-button widget, served from `widget/servicehub-bot.ts` with its types stripped
 *                          by Node itself at start-up, so there is no widget build step and no dependency
 *   GET  /bot/           → a try-out page, only when started with `tryPage` (development)
 *
 * It holds no credential and stores nothing. Logs say method, path, status and time, never the question.
 */
import { readFileSync, existsSync } from 'node:fs'
import { createServer } from 'node:http'
import { stripTypeScriptTypes } from 'node:module'
import type { IncomingMessage, Server, ServerResponse } from 'node:http'
import { ask, MAX_QUESTION_CHARS } from './answer.ts'
import type { KnowledgeBase } from './kb.ts'
import { createLiveProvider } from './live.ts'
import { httpReader, identityOf, ReadCache } from './servicehub.ts'
import type { ServiceHubReader } from './servicehub.ts'

export const PREFIX = '/bot'
/** A question is at most 500 characters; JSON around it fits easily in 4 KB. */
const MAX_BODY_BYTES = 4_096

export interface BotServerOptions {
  readonly kb: KnowledgeBase
  /** Where ServiceHub's API answers, e.g. `http://localhost:5153`. */
  readonly apiUrl: string
  /** Path of the widget's TypeScript source; `/bot/widget.js` answers 404 without it. */
  readonly widgetSource?: string
  /** Serve the try-out page at `/bot/`. For development only. */
  readonly tryPage?: boolean
  /** Questions per minute from one address. */
  readonly perMinute?: number
  /** For tests: build the reader for one request's headers instead of calling ServiceHub. */
  readonly readerFor?: (headers: IncomingMessage['headers']) => ServiceHubReader
  readonly log?: (line: string) => void
  /** `all`: one line per request. `problems` (what `run.sh` uses): only failed requests, so its terminal stays readable. */
  readonly logRequests?: 'all' | 'problems'
  readonly now?: () => number
}

export function createBotServer(options: BotServerOptions): Server {
  const cache = new ReadCache()
  const limiter = rateLimiter(options.perMinute ?? 30, options.now ?? Date.now)
  const log = options.log ?? ((line: string) => console.log(line))
  // The widget uses only erasable TypeScript, so stripping its types gives the browser plain JavaScript, done once here.
  const widget = options.widgetSource && existsSync(options.widgetSource)
    ? Buffer.from(stripTypeScriptTypes(readFileSync(options.widgetSource, 'utf8'), { mode: 'strip' }))
    : undefined

  const readerFor = async (headers: IncomingMessage['headers']): Promise<ServiceHubReader> =>
    options.readerFor?.(headers) ?? cache.for(await identityOf(headers), httpReader({ baseUrl: options.apiUrl, headers }))

  return createServer(async (req, res) => {
    const started = performance.now()
    const path = (req.url ?? '/').split('?')[0] ?? '/'
    try {
      await route(req, res, path)
    } catch (error) {
      if (!res.headersSent) send(res, 500, { error: 'The bot hit a problem answering that. Try again.' })
      log(`error ${error instanceof Error ? error.message : String(error)}`)
    } finally {
      if (options.logRequests !== 'problems' || res.statusCode >= 400) {
        log(`${req.method} ${path} ${res.statusCode} ${Math.round(performance.now() - started)}ms`)
      }
    }
  })

  async function route(req: IncomingMessage, res: ServerResponse, path: string): Promise<void> {
    if (path === `${PREFIX}/ask`) {
      if (req.method !== 'POST') return send(res, 405, { error: 'Ask with POST.' }, { allow: 'POST' })
      // JSON only. A browser cannot send this content type to another site's endpoint without a CORS preflight, which
      // this service never answers, so another website cannot make a person's browser ask the bot on their behalf.
      if (!/^application\/json\b/i.test(req.headers['content-type'] ?? '')) return send(res, 415, { error: 'Send the question as JSON.' })
      if (!limiter(clientAddress(req))) return send(res, 429, { error: 'Too many questions at once. Wait a minute and ask again.' }, { 'retry-after': '60' })

      const body = await readBody(req)
      if (body === 'too-large') return send(res, 413, { error: `A question can be at most ${MAX_QUESTION_CHARS} characters.` })
      const parsed = parseQuestion(body)
      if (typeof parsed === 'string') return send(res, 400, { error: parsed })

      const answer = await ask(options.kb, parsed.question, parsed.live ? { live: createLiveProvider(await readerFor(req.headers)) } : {})
      return send(res, 200, answer)
    }

    if (req.method !== 'GET' && req.method !== 'HEAD') return send(res, 405, { error: 'Not here.' }, { allow: 'GET' })
    if (path === `${PREFIX}/health`) return send(res, 200, { ok: true, knowledgeBase: options.kb.meta() })
    if (path === `${PREFIX}/widget.js` && widget) {
      return sendRaw(res, 200, widget, 'text/javascript; charset=utf-8', { 'cache-control': 'no-cache' })
    }
    if ((path === `${PREFIX}/` || path === PREFIX) && options.tryPage) return sendRaw(res, 200, Buffer.from(TRY_PAGE), 'text/html; charset=utf-8')
    return send(res, 404, { error: 'Not here.' })
  }
}

function parseQuestion(body: string): { question: string; live: boolean } | string {
  let value: unknown
  try {
    value = JSON.parse(body)
  } catch {
    return 'The question was not valid JSON.'
  }
  if (typeof value !== 'object' || value === null) return 'Send { "question": "…" }.'
  const { question, live } = value as { question?: unknown; live?: unknown }
  if (typeof question !== 'string' || !question.trim()) return 'Send { "question": "…" } with some words in it.'
  if (question.length > MAX_QUESTION_CHARS) return `A question can be at most ${MAX_QUESTION_CHARS} characters.`
  if (live !== undefined && typeof live !== 'boolean') return '"live" is true or false.'
  return { question, live: live ?? true }
}

async function readBody(req: IncomingMessage): Promise<string | 'too-large'> {
  const chunks: Buffer[] = []
  let size = 0
  for await (const chunk of req) {
    size += (chunk as Buffer).length
    if (size > MAX_BODY_BYTES) return 'too-large'
    chunks.push(chunk as Buffer)
  }
  return Buffer.concat(chunks).toString('utf8')
}

/** Every response: no caching (answers carry live numbers), no sniffing, no framing, nothing loaded from elsewhere. */
const SECURITY_HEADERS = {
  'cache-control': 'no-store',
  'x-content-type-options': 'nosniff',
  'x-frame-options': 'DENY',
  'referrer-policy': 'no-referrer',
  'content-security-policy': "default-src 'none'; script-src 'self'; style-src 'unsafe-inline'; connect-src 'self'; img-src 'self' data:; frame-ancestors 'none'",
} as const

function send(res: ServerResponse, status: number, body: unknown, extra: Record<string, string> = {}): void {
  sendRaw(res, status, Buffer.from(JSON.stringify(body)), 'application/json; charset=utf-8', extra)
}

function sendRaw(res: ServerResponse, status: number, body: Buffer, type: string, extra: Record<string, string> = {}): void {
  res.writeHead(status, { ...SECURITY_HEADERS, 'content-type': type, 'content-length': body.length, ...extra })
  res.end(res.req.method === 'HEAD' ? undefined : body)
}

/** A fixed window per address. In-memory: one process, and a restart forgets, which is fine for a help bot. */
function rateLimiter(perMinute: number, now: () => number): (key: string) => boolean {
  const windows = new Map<string, { start: number; count: number }>()
  return (key) => {
    const t = now()
    const w = windows.get(key)
    if (!w || t - w.start >= 60_000) {
      if (windows.size > 10_000) windows.clear()
      windows.set(key, { start: t, count: 1 })
      return true
    }
    w.count += 1
    return w.count <= perMinute
  }
}

/** The socket's address. `X-Forwarded-For` is not trusted: anyone can send it. Behind the proxy every request shares one address, which still caps a runaway loop. */
function clientAddress(req: IncomingMessage): string {
  return req.socket.remoteAddress ?? 'unknown'
}

const TRY_PAGE = `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>ServiceHub Bot: try it</title>
<link rel="icon" href="data:,">
<style>
  body { font: 15px/1.5 system-ui, sans-serif; margin: 0; padding: 32px 16px; background: #f8fafc; color: #0f172a; }
  main { max-width: 640px; margin: 0 auto; }
  code { background: #e2e8f0; padding: 1px 5px; border-radius: 4px; }
  @media (prefers-color-scheme: dark) { body { background: #0f172a; color: #e2e8f0; } code { background: #1e293b; } }
</style>
</head>
<body>
<main>
  <h1>ServiceHub Bot</h1>
  <p>A try-out page for the bot, served by the bot itself. Open the round button at the bottom right and ask, for example, <code>how do I replay dlq</code>.</p>
  <p>Live numbers come from the ServiceHub API this bot was started against, read with GET only. The bot guides; it never acts.</p>
</main>
<servicehub-bot></servicehub-bot>
<script type="module" src="/bot/widget.js"></script>
</body>
</html>
`
