import { execFileSync } from 'node:child_process'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { KnowledgeBase } from '../src/kb.ts'

export const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')

let built: string | undefined

/**
 * Builds a fresh knowledge base from the ServiceHub repo this project lives in, into a temp folder, once per test file.
 * Tests never read `data/knowledge.db`, so a stale build cannot make them pass.
 */
export function openFreshKb(): KnowledgeBase {
  if (!built) {
    built = join(mkdtempSync(join(tmpdir(), 'servicehub-bot-')), 'knowledge.db')
    execFileSync(process.execPath, [join(root, 'scripts/build-kb.ts')], { env: { ...process.env, KB_OUT: built }, stdio: 'pipe' })
  }
  return KnowledgeBase.open(built)
}
