/**
 * <servicehub-bot>: the floating "Ask" button and chat panel. One file, no dependencies, Shadow DOM so it cannot clash
 * with the page's styles.
 *
 *   <servicehub-bot endpoint="/bot/ask" live="true" app-base=""></servicehub-bot>
 *   <script type="module" src="/bot/widget.js"></script>
 *
 * Attributes
 * - `endpoint`  where to ask (default `/bot/ask`, same origin).
 * - `live`      `false` in ServiceHub's demo: answers come from the knowledge base only, no live numbers.
 * - `app-base`  the app's base path (`/demo` in the demo), put in front of app links and stripped to find the cloud.
 *
 * The bot guides; it never acts. Every link only opens a screen. A page that routes itself can catch the
 * `servicehub-bot:navigate` event (cancelable, `detail.href`) and navigate without a reload.
 *
 * Safety: an answer can contain names that came from a cloud (queues, reasons). Everything is escaped before any markup is
 * added, links come only from the answer's `links` list and are checked by `safeHref`, and no answer HTML is ever trusted.
 */

// ---- Pure parts (tested in Node: test/widget.test.ts) -------------------------------------------------------------

export interface BotLink { readonly label: string; readonly href: string }
export interface BotAnswer {
  readonly status: 'answered' | 'suggest' | 'unknown'
  readonly title?: string
  readonly markdown: string
  readonly links: readonly BotLink[]
  readonly suggestions: readonly string[]
  readonly live?: { readonly ok: boolean; readonly asOf?: string; readonly note?: string }
}

const CLOUDS = ['azure', 'aws', 'gcp'] as const
const DOCS = 'https://github.com/debdevops/servicehub/'

export function escapeHtml(text: string): string {
  return text.replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c] ?? c)
}

/** The cloud the person is looking at, from the address (`/azure/…`, `/demo/aws/…`); null on all-clouds pages. */
export function cloudFromPath(pathname: string, appBase = ''): string | null {
  const rest = appBase && pathname.startsWith(appBase) ? pathname.slice(appBase.length) : pathname
  const first = rest.split('/').filter(Boolean)[0] ?? ''
  return (CLOUDS as readonly string[]).includes(first) ? first : null
}

/**
 * Where a link may go, or null if nowhere. App links (`/…` or `?…`) get the app base, and `/all/…` becomes the cloud
 * being looked at. Outside links only to ServiceHub's own docs, opened in a new tab.
 */
export function safeHref(href: string, cloud: string | null, appBase = ''): { href: string; external: boolean } | null {
  if (href.startsWith('?')) return /^\?[\w=&%.-]*$/.test(href) ? { href, external: false } : null
  if (href.startsWith('/') && !href.startsWith('//') && !href.includes('\\')) {
    if (!/^\/[\w\-./?=&%~]*$/.test(href)) return null
    const scoped = cloud ? href.replace(/^\/all(?=\/|$|\?)/, `/${cloud}`) : href
    return { href: `${appBase}${scoped}`, external: false }
  }
  if (href.startsWith(DOCS) && /^[\w\-./:#?=&%~]+$/.test(href)) return { href, external: true }
  return null
}

/**
 * The small Markdown the knowledge base uses: paragraphs, `-` and `1.` lists (one level of nesting), ``` code blocks,
 * `code`, **bold**, *italic* / _italic_, and <https://…> links. Everything is escaped first; nothing else becomes markup.
 *
 * As in Markdown, a single line break inside a paragraph or a list item is a space (the docs are hard-wrapped, and bold
 * often spans the wrap); a blank line ends the paragraph.
 */
export function renderMarkdown(markdown: string): string {
  const out: string[] = []
  const lines = markdown.replace(/\r\n?/g, '\n').split('\n')
  let paragraph: string[] = []
  /** The text lines of the list item that is open, formatted together when it ends. */
  let itemText: string[] | null = null
  const lists: { tag: 'ul' | 'ol'; indent: number }[] = []

  const flushParagraph = () => {
    if (paragraph.length) out.push(`<p>${inline(paragraph.join(' '))}</p>`)
    paragraph = []
  }
  const flushItem = () => {
    if (itemText) out.push(inline(itemText.join(' ')))
    itemText = null
  }
  const closeLists = (toIndent = -1) => {
    flushItem()
    while (lists.length && (lists.at(-1)?.indent ?? 0) > toIndent) out.push(`</li></${lists.pop()?.tag}>`)
  }

  for (let i = 0; i < lines.length; i++) {
    // A quote marker is dropped and its text kept: the bubble has no quote style, and a stray ">" reads as noise.
    const line = (lines[i] ?? '').replace(/^\s*>\s?/, '')
    const fence = /^(\s*)```/.exec(line)
    if (fence) {
      flushParagraph()
      flushItem()
      const code: string[] = []
      const indent = fence[1]?.length ?? 0
      for (i++; i < lines.length && !/^\s*```/.test(lines[i] ?? ''); i++) code.push((lines[i] ?? '').slice(Math.min(indent, /^\s*/.exec(lines[i] ?? '')?.[0].length ?? 0)))
      // Focusable, so a code block wider than the bubble can be scrolled from the keyboard.
      out.push(`<pre tabindex="0"><code>${escapeHtml(code.join('\n'))}</code></pre>`)
      continue
    }
    const item = /^(\s*)([-*]|\d+\.)\s+(.*)$/.exec(line)
    if (item) {
      flushParagraph()
      flushItem()
      const indent = item[1]?.length ?? 0
      const tag = /\d/.test(item[2] ?? '') ? 'ol' : 'ul'
      const top = lists.at(-1)
      if (!top || indent > top.indent) {
        out.push(`<${tag}>`)
        lists.push({ tag, indent })
      } else {
        closeLists(indent)
        if (lists.at(-1)?.tag !== tag) {
          closeLists(indent - 1)
          out.push(`<${tag}>`)
          lists.push({ tag, indent })
        } else {
          out.push('</li>')
        }
      }
      out.push('<li>')
      itemText = [item[3] ?? '']
      continue
    }
    if (!line.trim()) {
      flushParagraph()
      closeLists()
      continue
    }
    if (lists.length && /^\s+/.test(line)) {
      // A wrapped list item (or text after a code block inside one) continues that item.
      if (itemText) itemText.push(line.trim())
      else itemText = [line.trim()]
      continue
    }
    closeLists()
    paragraph.push(line.trim())
  }
  flushParagraph()
  closeLists()
  return out.join('')
}

const CODE_TOKEN = /\u0000(\d+)\u0000/g

function inline(text: string): string {
  // Code spans are set aside first, so nothing inside them becomes markup, yet bold and italic can wrap them
  // ("**`IRecoveryLedger`**", "**It answers only to `localhost`.**"). The tokens use a character no answer contains.
  const codes: string[] = []
  const tokenised = text.replace(/\u0000/g, '').replace(/`([^`]+)`/g, (_m, code: string) => {
    codes.push(`<code>${escapeHtml(code)}</code>`)
    return `\u0000${codes.length - 1}\u0000`
  })
  return escapeHtml(tokenised)
    // [text](url) keeps its text only: links come from the answer's checked `links` list, never from its prose.
    .replace(/\[([^\]]+)\]\([^)\s]*\)/g, '$1')
    .replace(/&lt;(https?:\/\/[^\s&]+)&gt;/g, (_m, url: string) => `<a href="${url}" target="_blank" rel="noopener noreferrer">${url}</a>`)
    .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
    .replace(/(^|[^\w*])\*(?!\s)(.+?)\*(?!\w)/g, '$1<em>$2</em>')
    .replace(/(^|[^\w])_(?!\s)(.+?)_(?!\w)/g, '$1<em>$2</em>')
    .replace(CODE_TOKEN, (_m, i: string) => codes[Number(i)] ?? '')
}

/** "as of 17:20" in the person's own time. */
export function asOfWords(iso: string | undefined): string {
  if (!iso) return ''
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? '' : d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
}

// ---- The element (browser only) ----------------------------------------------------------------------------------

const STYLES = `
:host { --bot-accent: #2563eb; --bot-accent-text: #fff; --bot-bg: #fff; --bot-fg: #0f172a; --bot-muted: #64748b; --bot-line: #e2e8f0;
  --bot-soft: #f1f5f9; --bot-bot: #f8fafc; --bot-warn: #b45309; position: fixed; right: 20px; bottom: 20px; z-index: 2147483000;
  font: 14px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif; color: var(--bot-fg); }
:host { --bot-link: var(--bot-accent); }
@media (prefers-color-scheme: dark) { :host { --bot-bg: #0f172a; --bot-fg: #e2e8f0; --bot-muted: #94a3b8; --bot-line: #334155;
  --bot-soft: #1e293b; --bot-bot: #111c33; --bot-warn: #fbbf24; --bot-link: #93c5fd; } }
* { box-sizing: border-box; }
button { font: inherit; }
.fab { width: 56px; height: 56px; border-radius: 50%; border: 0; background: var(--bot-accent); color: var(--bot-accent-text);
  box-shadow: 0 6px 20px rgb(0 0 0 / .25); cursor: pointer; display: grid; place-items: center; }
.fab:focus-visible, button:focus-visible, a:focus-visible, input:focus-visible, .log:focus-visible, pre:focus-visible { outline: 3px solid #93c5fd; outline-offset: 2px; }
.panel { position: absolute; right: 0; bottom: 68px; width: min(400px, calc(100vw - 32px)); height: min(600px, calc(100vh - 110px));
  background: var(--bot-bg); border: 1px solid var(--bot-line); border-radius: 16px; box-shadow: 0 16px 48px rgb(0 0 0 / .25);
  display: flex; flex-direction: column; overflow: hidden; }
.panel[hidden] { display: none; }
.head { padding: 12px 14px; border-bottom: 1px solid var(--bot-line); display: flex; gap: 10px; align-items: start; }
.head h2 { margin: 0; font-size: 15px; }
.head p { margin: 2px 0 0; color: var(--bot-muted); font-size: 12px; }
.close { margin-left: auto; background: none; border: 0; color: var(--bot-muted); cursor: pointer; font-size: 20px; line-height: 1; padding: 2px 6px; border-radius: 6px; }
.log { flex: 1; overflow-y: auto; padding: 14px; display: flex; flex-direction: column; gap: 12px; }
.msg { max-width: 92%; padding: 9px 12px; border-radius: 12px; overflow-wrap: anywhere; }
.me { align-self: flex-end; background: var(--bot-accent); color: var(--bot-accent-text); border-bottom-right-radius: 4px; }
.bot { align-self: flex-start; background: var(--bot-bot); border: 1px solid var(--bot-line); border-bottom-left-radius: 4px; }
.bot h3 { margin: 0 0 6px; font-size: 14px; }
.bot p { margin: 0 0 8px; } .bot p:last-child { margin-bottom: 0; }
.bot ul, .bot ol { margin: 0 0 8px; padding-left: 20px; }
.bot code { background: var(--bot-soft); padding: 0 4px; border-radius: 4px; font-size: 12.5px; }
.bot pre { background: var(--bot-soft); padding: 8px; border-radius: 8px; overflow-x: auto; margin: 0 0 8px; }
.bot pre code { padding: 0; background: none; }
.bot a { color: var(--bot-link); }
.live { font-size: 12px; color: var(--bot-muted); margin-top: 6px; }
.live.bad { color: var(--bot-warn); }
.links { display: flex; flex-direction: column; gap: 6px; margin-top: 10px; }
.links a { display: block; padding: 7px 10px; border-radius: 8px; border: 1px solid var(--bot-link); color: var(--bot-link);
  text-decoration: none; font-weight: 600; font-size: 13px; }
.links a:hover { background: var(--bot-soft); }
.chips { display: flex; flex-wrap: wrap; gap: 6px; }
.chips button { border: 1px solid var(--bot-line); background: var(--bot-bg); color: var(--bot-fg); border-radius: 999px; padding: 4px 10px;
  font-size: 12.5px; cursor: pointer; text-align: left; }
.chips button:hover { background: var(--bot-soft); }
.busy { color: var(--bot-muted); font-style: italic; }
form { display: flex; gap: 8px; padding: 10px; border-top: 1px solid var(--bot-line); }
input { flex: 1; min-width: 0; padding: 9px 11px; border-radius: 10px; border: 1px solid var(--bot-line); background: var(--bot-bg); color: var(--bot-fg); font: inherit; }
form button { border: 0; border-radius: 10px; padding: 0 14px; background: var(--bot-accent); color: var(--bot-accent-text); font-weight: 600; cursor: pointer; }
form button:disabled { opacity: .5; cursor: default; }
.sr { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
`

const STARTERS = ['How do I replay a DLQ?', 'How many dead letters do I have?', 'Did my replays work?', 'Which clouds are connected?']
const ICON_CHAT = '<svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/></svg>'
const ICON_CLOSE = '<svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18"/></svg>'
const ASK_TIMEOUT_MS = 15_000

export function defineServiceHubBot(): void {
  if (typeof customElements === 'undefined' || customElements.get('servicehub-bot')) return

  class ServiceHubBot extends HTMLElement {
    #root = this.attachShadow({ mode: 'open' })
    #panel!: HTMLElement
    #log!: HTMLElement
    #input!: HTMLInputElement
    #send!: HTMLButtonElement
    #fab!: HTMLButtonElement
    #greeted = false
    #busy = false

    connectedCallback(): void {
      if (this.#fab) return
      const sheet = new CSSStyleSheet()
      sheet.replaceSync(STYLES)
      this.#root.adoptedStyleSheets = [sheet]
      this.#root.innerHTML = `
        <div class="panel" id="panel" role="dialog" aria-modal="false" aria-labelledby="title" hidden>
          <div class="head">
            <div>
              <h2 id="title">ServiceHub Bot</h2>
              <p>I guide; you act. I never change anything.</p>
            </div>
            <button class="close" type="button" aria-label="Close the bot">×</button>
          </div>
          <div class="log" role="log" aria-live="polite" aria-relevant="additions" aria-label="Conversation" tabindex="0"></div>
          <form>
            <label class="sr" for="q">Your question</label>
            <input id="q" name="q" autocomplete="off" maxlength="500" placeholder="Ask about ServiceHub…">
            <button type="submit">Ask</button>
          </form>
        </div>
        <button class="fab" type="button" aria-label="Ask ServiceHub Bot" aria-expanded="false" aria-controls="panel">${ICON_CHAT}</button>`
      this.#panel = this.#root.querySelector('.panel') as HTMLElement
      this.#log = this.#root.querySelector('.log') as HTMLElement
      this.#input = this.#root.querySelector('input') as HTMLInputElement
      this.#send = this.#root.querySelector('form button') as HTMLButtonElement
      this.#fab = this.#root.querySelector('.fab') as HTMLButtonElement

      this.#fab.addEventListener('click', () => this.toggle())
      ;(this.#root.querySelector('.close') as HTMLElement).addEventListener('click', () => this.toggle(false))
      ;(this.#root.querySelector('form') as HTMLFormElement).addEventListener('submit', (e) => {
        e.preventDefault()
        void this.ask(this.#input.value)
      })
      this.#root.addEventListener('keydown', (e) => {
        if ((e as KeyboardEvent).key === 'Escape' && !this.#panel.hidden) this.toggle(false)
      })
      // Keys typed in the bot are the bot's. Leaving the Shadow DOM they would look as if they came from <servicehub-bot>,
      // not a text field, and fire the page's own shortcuts ("?" opens ServiceHub's Help, "/" its search).
      for (const type of ['keydown', 'keyup', 'keypress'] as const) this.#root.addEventListener(type, (e) => e.stopPropagation())
    }

    get #endpoint(): string { return this.getAttribute('endpoint') || '/bot/ask' }
    get #live(): boolean { return this.getAttribute('live') !== 'false' }
    get #appBase(): string { return (this.getAttribute('app-base') ?? '').replace(/\/$/, '') }

    toggle(open = this.#panel.hidden): void {
      this.#panel.hidden = !open
      this.#fab.setAttribute('aria-expanded', String(open))
      this.#fab.innerHTML = open ? ICON_CLOSE : ICON_CHAT
      this.#fab.setAttribute('aria-label', open ? 'Close ServiceHub Bot' : 'Ask ServiceHub Bot')
      if (open && !this.#greeted) {
        this.#greeted = true
        this.#botMessage({
          status: 'answered',
          markdown: "Hi! Ask me anything about ServiceHub: dead letters, replays, connecting clouds, running it. I'll show you what's true right now and where to click. I never change anything myself.",
          links: [],
          suggestions: STARTERS,
        })
      }
      if (open) this.#input.focus()
      else this.#fab.focus()
    }

    async ask(raw: string): Promise<void> {
      const question = raw.trim().slice(0, 500)
      if (!question || this.#busy) return
      this.#busy = true
      this.#send.disabled = true
      this.#input.value = ''
      this.#append('div', 'msg me', question)
      const thinking = this.#append('div', 'msg bot busy', 'Looking…')
      try {
        const response = await fetch(this.#endpoint, {
          method: 'POST',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify({ question, live: this.#live }),
          credentials: 'same-origin',
          signal: AbortSignal.timeout(ASK_TIMEOUT_MS),
        })
        const body = (await response.json().catch(() => ({}))) as BotAnswer & { error?: string }
        thinking.remove()
        if (!response.ok) {
          // 502–504 come from the proxy in front of the bot: the bot itself is not running. ServiceHub is unaffected.
          const down = response.status >= 502 && response.status <= 504
          const markdown = body.error ?? (down ? "ServiceHub Bot isn't running right now. ServiceHub itself is fine; restart `./run.sh` to bring the bot back." : "The bot couldn't answer that right now.")
          this.#botMessage({ status: 'unknown', markdown, links: [], suggestions: [] })
        }
        else this.#botMessage(body)
      } catch {
        thinking.remove()
        this.#botMessage({ status: 'unknown', markdown: "The bot isn't reachable right now. Try again in a moment.", links: [], suggestions: [] })
      } finally {
        this.#busy = false
        this.#send.disabled = false
        this.#input.focus()
      }
    }

    #botMessage(answer: BotAnswer): void {
      const box = this.#append('div', 'msg bot', '')
      // The only innerHTML of an answer: renderMarkdown escapes everything before adding its own few tags.
      box.innerHTML = (answer.title && answer.status === 'answered' ? `<h3>${escapeHtml(answer.title)}</h3>` : '') + renderMarkdown(answer.markdown)

      if (answer.live) {
        const note = document.createElement('div')
        note.className = answer.live.ok ? 'live' : 'live bad'
        note.textContent = answer.live.ok ? `Live numbers from your ServiceHub, as of ${asOfWords(answer.live.asOf)}.` : 'Live numbers could not be read (see above).'
        box.append(note)
      }

      const cloud = cloudFromPath(location.pathname, this.#appBase)
      const links = answer.links.map((l) => ({ label: l.label, to: safeHref(l.href, cloud, this.#appBase) })).filter((l) => l.to)
      if (links.length) {
        const row = document.createElement('div')
        row.className = 'links'
        for (const { label, to } of links) {
          const a = document.createElement('a')
          a.textContent = label
          a.href = to!.href
          if (to!.external) {
            a.target = '_blank'
            a.rel = 'noopener noreferrer'
          } else {
            a.addEventListener('click', (e) => {
              const go = new CustomEvent('servicehub-bot:navigate', { detail: { href: to!.href }, bubbles: true, composed: true, cancelable: true })
              if (!this.dispatchEvent(go)) e.preventDefault()
            })
          }
          row.append(a)
        }
        box.append(row)
      }

      if (answer.suggestions.length) {
        const chips = document.createElement('div')
        chips.className = 'chips'
        chips.setAttribute('aria-label', answer.status === 'answered' ? 'Related questions' : 'Did you mean')
        for (const s of answer.suggestions.slice(0, 4)) {
          const b = document.createElement('button')
          b.type = 'button'
          b.textContent = s
          b.addEventListener('click', () => void this.ask(s))
          chips.append(b)
        }
        this.#log.append(chips)
      }
      // Show the start of the answer, not its end: the live numbers and the first step come first.
      this.#log.scrollTop = Math.max(0, box.offsetTop - this.#log.offsetTop - 8)
    }

    #append(tag: string, cls: string, text: string): HTMLElement {
      const el = document.createElement(tag)
      el.className = cls
      el.textContent = text
      this.#log.append(el)
      this.#log.scrollTop = this.#log.scrollHeight
      return el
    }
  }

  customElements.define('servicehub-bot', ServiceHubBot)
}

defineServiceHubBot()
