import assert from 'node:assert/strict'
import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, it } from 'node:test'
import { root } from './helpers.ts'

/**
 * The bot guides; it never acts. This guard reads every source file and fails if anything could send a request that
 * changes state, or could open the knowledge base for writing at run time. Only the build script writes the DB.
 *
 * The bot's own endpoint (`POST /bot/ask`) *receives* a question; that is not a request to ServiceHub. What is checked
 * here is every request the bot *sends*.
 */
describe('guide only: nothing the bot runs can change anything', () => {
  const files = ['src', 'widget']
    .flatMap((dir) =>
      readdirSync(join(root, dir), { recursive: true, encoding: 'utf8' })
        .filter((f) => f.endsWith('.ts') && !f.includes('dist/'))
        .map((f) => ({ name: `${dir}/${f}`, text: readFileSync(join(root, dir, f), 'utf8') })),
    )

  it('finds the source files', () => assert.ok(files.length >= 6))

  it('talks to ServiceHub from one file only', () => {
    const fetching = files.filter((f) => /\bfetch\s*\(|\bdoFetch\s*\(/.test(f.text) && f.name.startsWith('src/'))
    assert.deepEqual(fetching.map((f) => f.name), ['src/servicehub.ts'])
  })

  for (const { name, text } of files) {
    it(`${name} uses no HTTP client but fetch`, () => {
      assert.doesNotMatch(text, /from ['"](axios|got|undici|node-fetch|superagent)['"]/)
      assert.doesNotMatch(text, /\bhttps?\.(request|get)\s*\(/)
      assert.doesNotMatch(text, /\.(post|put|patch)\s*\(/)
    })

    if (name.startsWith('src/')) {
      it(`${name} sends GET and nothing else`, () => {
        for (const m of text.matchAll(/method\s*:\s*['"`](\w+)['"`]/gi)) assert.equal(m[1], 'GET', `${name}: ${m[0]}`)
      })
    } else {
      it(`${name} sends only the question to the bot, and nothing to ServiceHub`, () => {
        for (const m of text.matchAll(/method\s*:\s*['"`](\w+)['"`]/gi)) assert.equal(m[1], 'POST', `${name}: ${m[0]}`)
        assert.doesNotMatch(text, /\/api\/v1/)
      })
    }

    it(`${name} opens the knowledge base read-only`, () => {
      for (const m of text.matchAll(/new DatabaseSync\(([^)]*)\)/g)) {
        assert.match(m[1] ?? '', /readOnly:\s*true/, `${name}: ${m[0]}`)
      }
    })
  }
})
