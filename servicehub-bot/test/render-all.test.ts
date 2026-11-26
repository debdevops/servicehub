import assert from 'node:assert/strict'
import { existsSync, readdirSync } from 'node:fs'
import { dirname } from 'node:path'
import { DatabaseSync } from 'node:sqlite'
import { describe, it } from 'node:test'
import { renderMarkdown } from '../widget/servicehub-bot.ts'
import { freshKbPath } from './helpers.ts'

/**
 * Every answer in a freshly built knowledge base, rendered exactly as the widget renders it. Found in the end-to-end run of
 * 2026-11-15: doc sections showed raw ">", "[text](url)" and table pipes in the bubble. This keeps a new doc from bringing
 * that back.
 */
const ALLOWED_TAG = /^<\/?(p|br|ul|ol|li|strong|em|code)>$|^<pre tabindex="0">$|^<\/pre>$|^<a href="https?:\/\/[^"<>\s]+" target="_blank" rel="noopener noreferrer">$|^<\/a>$/

function answers(): { id: string; answer: string }[] {
  const db = new DatabaseSync(freshKbPath(), { readOnly: true })
  try {
    return db.prepare('SELECT id, answer FROM entries ORDER BY id').all() as { id: string; answer: string }[]
  } finally {
    db.close()
  }
}

describe('every answer in the knowledge base renders cleanly in the widget', () => {
  const all = answers()

  it('has answers to check', () => assert.ok(all.length > 150, String(all.length)))

  for (const { id, answer } of all) {
    it(id, () => {
      const html = renderMarkdown(answer)
      for (const tag of html.match(/<[^>]*>/g) ?? []) assert.match(tag, ALLOWED_TAG, `unexpected tag ${tag}`)
      // What a person reads outside code blocks: tags removed, entities decoded.
      const text = html
        .replace(/<pre[^>]*>[\s\S]*?<\/pre>/g, '')
        .replace(/<code>[\s\S]*?<\/code>/g, '')
        .replace(/<[^>]*>/g, '\n')
        .replace(/&gt;/g, '>').replace(/&lt;/g, '<').replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&amp;/g, '&')
      assert.doesNotMatch(text, /^\s*>\s/m, 'a quote marker')
      assert.doesNotMatch(text, /\]\(/, 'a [text](url) link')
      assert.doesNotMatch(text, /^\s*\|.*\|\s*$|\|\s*-{3}/m, 'a table row')
      assert.doesNotMatch(text, /^#{1,6}\s/m, 'a heading marker')
      assert.doesNotMatch(text, /\*\*/, 'unrendered bold')
    })
  }
})

describe('building the knowledge base', () => {
  it('swaps the file in whole and leaves no half-built file behind', () => {
    const folder = dirname(freshKbPath())
    assert.ok(existsSync(freshKbPath()))
    assert.deepEqual(readdirSync(folder).filter((f) => f.includes('.building-')), [])
  })
})
