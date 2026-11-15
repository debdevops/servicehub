/**
 * Ask the bot from a terminal.
 *
 *   npm run ask -- "how do I replay dlq"
 *   npm run ask -- --live "how do I replay dlq"       (adds live numbers from ServiceHub, GET only)
 *   npm run ask -- --debug "why aws shows nothing"    (also prints the ranked hits and their scores)
 *   npm run ask -- --json "rotate encryption key"     (the exact payload the widget will get)
 *
 * `--live` reads SERVICEHUB_API_URL (default http://localhost:5153). If ServiceHub needs an API key, set SERVICEHUB_API_KEY;
 * it is sent as X-API-KEY and the bot sees only what that key may see.
 */
import { parseArgs } from 'node:util'
import { resolve } from 'node:path'
import { ask } from './answer.ts'
import { KnowledgeBase } from './kb.ts'
import { createLiveProvider } from './live.ts'
import { httpReader } from './servicehub.ts'

const { values, positionals } = parseArgs({
  allowPositionals: true,
  options: {
    db: { type: 'string', default: process.env.KB_PATH ?? 'data/knowledge.db' },
    live: { type: 'boolean', default: false },
    api: { type: 'string', default: process.env.SERVICEHUB_API_URL ?? 'http://localhost:5153' },
    json: { type: 'boolean', default: false },
    debug: { type: 'boolean', default: false },
  },
})

const question = positionals.join(' ').trim()
if (!question) {
  console.error('Usage: npm run ask -- [--live] [--debug] [--json] "your question"')
  process.exit(2)
}

const live = values.live
  ? createLiveProvider(httpReader({ baseUrl: values.api, headers: { 'x-api-key': process.env.SERVICEHUB_API_KEY } }))
  : undefined

const kb = KnowledgeBase.open(resolve(values.db))
try {
  const answer = await ask(kb, question, live ? { live } : {})
  if (values.json) {
    console.log(JSON.stringify(answer, null, 2))
  } else {
    console.log(`[${answer.status}]${answer.title ? ` ${answer.title}` : ''}${answer.live ? `  (live ${answer.live.ok ? 'read' : 'NOT read'} at ${answer.live.asOf})` : ''}\n`)
    console.log(answer.markdown)
    for (const link of answer.links) console.log(`  → ${link.label}: ${link.href}`)
    if (answer.suggestions.length) console.log(`\nAlso: ${answer.suggestions.join(' | ')}`)
    if (answer.source) console.log(`\n(source: ${answer.source.ref}, ${answer.source.id})`)
  }
  if (values.debug) {
    const { groups, hits } = kb.search(question, 8)
    console.log(`\nterms: ${groups.map((g) => g.join('/')).join('  ')}`)
    for (const h of hits) console.log(`  ${h.score.toFixed(2).padStart(7)}  cov ${h.coverage.toFixed(2)}  ${h.entry.kind.padEnd(7)} ${h.entry.id}`)
  }
} finally {
  kb.close()
}
