/**
 * Start the bot service.
 *
 *   npm run serve                 # http://127.0.0.1:5160/bot/…, live numbers from http://localhost:5153
 *   npm run serve -- --try        # also serves a try-out page at /bot/
 *
 * ServiceHub's `./run.sh` starts it too (option (c) of 2026-11-15: the bot comes with run.sh and development only; the
 * Docker image does not include it).
 *
 * Environment: BOT_PORT (5160), BOT_HOST (127.0.0.1), SERVICEHUB_API_URL (http://localhost:5153), KB_PATH
 * (data/knowledge.db, relative to this project), BOT_LOG (`all` to log every request; otherwise only failed ones).
 * It listens on this machine only unless BOT_HOST says otherwise.
 */
import { resolve } from 'node:path'
import { parseArgs } from 'node:util'
import { KnowledgeBase } from './kb.ts'
import { createBotServer, PREFIX } from './server.ts'

const project = resolve(import.meta.dirname, '..')
const { values } = parseArgs({ options: { try: { type: 'boolean', default: false } } })
const port = Number(process.env.BOT_PORT ?? 5160)
const host = process.env.BOT_HOST ?? '127.0.0.1'
const apiUrl = process.env.SERVICEHUB_API_URL ?? 'http://localhost:5153'

const kb = KnowledgeBase.open(resolve(project, process.env.KB_PATH ?? 'data/knowledge.db'))
const server = createBotServer({
  kb,
  apiUrl,
  widgetSource: resolve(project, 'widget/servicehub-bot.ts'),
  tryPage: values.try,
  // Every request with --try or BOT_LOG=all; otherwise only failed ones (run.sh's terminal is shared with the API and Vite).
  logRequests: values.try || process.env.BOT_LOG === 'all' ? 'all' : 'problems',
})

server.on('error', (error: NodeJS.ErrnoException) => {
  console.error(error.code === 'EADDRINUSE' ? `ServiceHub Bot: port ${port} is already in use. Set BOT_PORT to another port.` : `ServiceHub Bot: ${error.message}`)
  process.exit(1)
})

server.listen(port, host, () => {
  const meta = kb.meta()
  console.log(`ServiceHub Bot on http://${host}:${port}${PREFIX}/ (knowledge base built ${meta.built_at}, ServiceHub ${meta.servicehub_commit}; live numbers from ${apiUrl}, GET only)`)
  if (values.try) console.log(`Try it: http://${host}:${port}${PREFIX}/`)
})

for (const signal of ['SIGINT', 'SIGTERM'] as const) {
  process.on(signal, () => {
    server.close(() => {
      kb.close()
      process.exit(0)
    })
    // A proxy keeps connections open; without this, close() would wait for them.
    server.closeAllConnections()
  })
}
