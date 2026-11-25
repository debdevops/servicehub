import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { renderMarkdown as renderProse } from '../widget/servicehub-bot.ts'
import { cleanMarkdown } from '../scripts/clean-markdown.ts'

describe('cleanMarkdown (doc sections → what the chat bubble can show)', () => {
  it('drops quote markers and keeps the text', () => {
    assert.equal(cleanMarkdown('> **In this article:** what a backup is.\n>\n> Who this is for.'), '**In this article:** what a backup is.\n\nWho this is for.')
  })
  it('keeps the text of a link, drops its address', () => {
    assert.equal(cleanMarkdown('Use the [VM runbook](HOSTING-AZURE.md#vm) instead.'), 'Use the VM runbook instead.')
  })
  it('keeps <https://…> links and drops HTML', () => {
    assert.equal(cleanMarkdown('Open <http://localhost:8080>. <details><summary>More</summary>x</details>'), 'Open <http://localhost:8080>. Morex')
  })
  it('drops images', () => assert.equal(cleanMarkdown('Before ![shot](a.png) after'), 'Before  after'))
  it('turns a table into one list line per row, without its header and separator', () => {
    assert.equal(
      cleanMarkdown('| Part | Meaning |\n|---|---|\n| `-d` | Run in the background |\n| `-p 127.0.0.1:8080:8080` | This machine only |'),
      '- `-d` — Run in the background\n- `-p 127.0.0.1:8080:8080` — This machine only',
    )
  })
  it('turns deep headings into bold lines', () => assert.equal(cleanMarkdown('#### Before you start'), '**Before you start**'))
  it('leaves code blocks exactly as written', () => {
    const code = '```bash\n> not a quote\n| not | a table |\n[not](a link)\n```'
    assert.equal(cleanMarkdown(code), code)
  })
})


it('nested fragments cannot rebuild a tag after cleaning', () => {
  assert.doesNotMatch(cleanMarkdown('a <scr<script>ipt>alert(1)</scr</script>ipt> b'), /<script/i)
})

it('only loopback autolinks render; an external <https://…> in prose stays text', () => {
  assert.doesNotMatch(renderProse('see <https://evil.example/login>'), /<a /)
  assert.match(renderProse('open <http://localhost:3000>'), /<a href="http:\/\/localhost:3000"/)
})
