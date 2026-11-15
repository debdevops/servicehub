import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { cloudFromPath, escapeHtml, renderMarkdown, safeHref } from '../widget/servicehub-bot.ts'

describe('renderMarkdown is safe', () => {
  // Names in live answers come from a cloud (queue names, reasons). None of them may become markup.
  const hostile = [
    '<script>alert(1)</script>',
    '<img src=x onerror=alert(1)>',
    '**<b onmouseover=alert(1)>x</b>**',
    '`</code><script>x</script>`',
    '[click](javascript:alert(1))',
    '<javascript:alert(1)>',
    '<data:text/html,<script>alert(1)</script>>',
    '```\n<script>alert(1)</script>\n```',
    '- <iframe src=//evil>',
    '"><svg onload=alert(1)>',
  ]
  const ALLOWED_TAG = /^<\/?(p|br|ul|ol|li|strong|em|code)>$|^<pre tabindex="0">$|^<\/pre>$|^<a href="https?:\/\/[^"<>\s]+" target="_blank" rel="noopener noreferrer">$|^<\/a>$/
  for (const input of hostile) {
    it(`escapes ${JSON.stringify(input)}`, () => {
      const html = renderMarkdown(input)
      for (const tag of html.match(/<[^>]*>/g) ?? []) assert.match(tag, ALLOWED_TAG, `unexpected tag ${tag} in ${html}`)
      assert.doesNotMatch(html, /<a href="(?!https?:)/)
    })
  }

  it('makes links only from <http(s)://…>', () => {
    assert.equal(renderMarkdown('Open <http://localhost:3000>.'), '<p>Open <a href="http://localhost:3000" target="_blank" rel="noopener noreferrer">http://localhost:3000</a>.</p>')
    assert.doesNotMatch(renderMarkdown('[x](https://evil.example)'), /<a /)
  })
})

describe('renderMarkdown formats the knowledge base\'s Markdown', () => {
  it('bold, italic, code', () => {
    assert.equal(renderMarkdown('**Replay** then *wait* or _not_, run `npm test`'), '<p><strong>Replay</strong> then <em>wait</em> or <em>not</em>, run <code>npm test</code></p>')
  })
  it('does not format inside code', () => {
    assert.equal(renderMarkdown('`**not bold**`'), '<p><code>**not bold**</code></p>')
  })
  it('keeps underscores inside names', () => {
    assert.equal(renderMarkdown('queue my_orders_dlq'), '<p>queue my_orders_dlq</p>')
  })
  it('lists, with one level of nesting', () => {
    assert.equal(
      renderMarkdown('- **Shop**: 120\n  - by reason: 90 X\n- **Audit**: 2'),
      '<ul><li><strong>Shop</strong>: 120<ul><li>by reason: 90 X</li></ul></li><li><strong>Audit</strong>: 2</li></ul>',
    )
  })
  it('numbered steps', () => {
    assert.equal(renderMarkdown('1. Open\n2. Replay'), '<ol><li>Open</li><li>Replay</li></ol>')
  })
  it('code blocks, indented under a list item too', () => {
    assert.equal(renderMarkdown('2. Start it:\n   ```bash\n   docker run x\n   ```\n3. Done'), '<ol><li>Start it:<pre tabindex="0"><code>docker run x</code></pre></li><li>Done</li></ol>')
  })
  it('drops quote markers, but never inside a code block', () => {
    assert.equal(renderMarkdown('> **Short answer:** it works.'), '<p><strong>Short answer:</strong> it works.</p>')
    assert.equal(renderMarkdown('```\n> Healthy\n```'), '<pre tabindex="0"><code>&gt; Healthy</code></pre>')
  })
  it('keeps the text of a [text](url) link, never the link', () => {
    assert.equal(renderMarkdown('Use the [VM runbook](HOSTING-AZURE.md#vm).'), '<p>Use the VM runbook.</p>')
  })
  it('paragraphs: a blank line ends one, a single line break is a space (hard-wrapped docs)', () => {
    assert.equal(renderMarkdown('One.\n\nTwo\nlines.'), '<p>One.</p><p>Two lines.</p>')
  })
  it('bold across a hard wrap', () => {
    assert.equal(renderMarkdown('lives in **one\nSQLite file**, here.'), '<p>lives in <strong>one SQLite file</strong>, here.</p>')
  })
  it('bold around code, and code inside bold', () => {
    assert.equal(renderMarkdown('- **`IRecoveryLedger`** — the header'), '<ul><li><strong><code>IRecoveryLedger</code></strong> — the header</li></ul>')
    assert.equal(renderMarkdown('**It answers only to `localhost` and `[::1]`.**'), '<p><strong>It answers only to <code>localhost</code> and <code>[::1]</code>.</strong></p>')
  })
  it('a wrapped list item stays one item', () => {
    assert.equal(renderMarkdown('1. **Declare it —\n   honestly.** Add a preset.\n2. Next'), '<ol><li><strong>Declare it — honestly.</strong> Add a preset.</li><li>Next</li></ol>')
  })
})

describe('safeHref', () => {
  it('scopes /all/ links to the cloud being looked at', () => {
    assert.deepEqual(safeHref('/all/replays', 'aws'), { href: '/aws/replays', external: false })
    assert.deepEqual(safeHref('/all', 'gcp'), { href: '/gcp', external: false })
    assert.deepEqual(safeHref('/all/replays', null), { href: '/all/replays', external: false })
    assert.deepEqual(safeHref('/allies', 'aws'), { href: '/allies', external: false })
  })
  it('puts the app base in front (the demo)', () => {
    assert.deepEqual(safeHref('/all/replays', 'aws', '/demo'), { href: '/demo/aws/replays', external: false })
  })
  it('keeps a deep link to a connection\'s tab', () => {
    const href = '/azure/c/service-bus/aaaaaaaa-0000?tab=dead-letters&reason=MaxDeliveryCountExceeded'
    assert.deepEqual(safeHref(href, 'azure'), { href, external: false })
  })
  it('keeps overlay links', () => assert.deepEqual(safeHref('?panel=help', null), { href: '?panel=help', external: false }))
  it('allows ServiceHub\'s own docs, in a new tab', () => {
    assert.deepEqual(safeHref('https://github.com/debdevops/servicehub/blob/main/docs/DOCKER.md#security', null), {
      href: 'https://github.com/debdevops/servicehub/blob/main/docs/DOCKER.md#security',
      external: true,
    })
  })
  it('refuses everything else', () => {
    for (const bad of ['javascript:alert(1)', '//evil.example/x', 'https://evil.example', 'https://github.com/someone-else/x', '/\\evil', '/x"onmouseover="a', 'data:text/html,x', '?a=<b>']) {
      assert.equal(safeHref(bad, 'aws'), null, bad)
    }
  })
})

describe('cloudFromPath', () => {
  it('reads the first segment, after the app base', () => {
    assert.equal(cloudFromPath('/aws/attention'), 'aws')
    assert.equal(cloudFromPath('/demo/gcp/c/pubsub/x', '/demo'), 'gcp')
    assert.equal(cloudFromPath('/connections'), null)
    assert.equal(cloudFromPath('/'), null)
    assert.equal(cloudFromPath('/all/replays'), null)
  })
})

describe('escapeHtml', () => {
  it('escapes the five characters', () => assert.equal(escapeHtml(`<a href="x">'&'</a>`), '&lt;a href=&quot;x&quot;&gt;&#39;&amp;&#39;&lt;/a&gt;'))
})
