/**
 * Builds data/knowledge.db from three sources, strongest first:
 *   1. knowledge/entries.ts                                  hand-written answers (some with live data)
 *   2. apps/servicehub/src/next/content/help.ts              the app's own Help answers, so wording stays the product's
 *   3. docs/**.md                                           every doc section, as a fallback with a link to read more
 * Links to app pages are checked against apps/servicehub/src/next/nav/navigation.ts.
 *
 * Usage: npm run build:kb   (SERVICEHUB_REPO defaults to the parent folder, the ServiceHub repo this project lives in;
 * KB_OUT to data/knowledge.db). It only reads the rest of the repo; it writes nothing outside servicehub-bot/.
 */
import { execFileSync } from 'node:child_process'
import { existsSync, mkdirSync, readdirSync, readFileSync, renameSync, rmSync } from 'node:fs'
import { dirname, join, relative, resolve } from 'node:path'
import { DatabaseSync } from 'node:sqlite'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { entries as curated } from '../knowledge/entries.ts'
import { helpAliases } from '../knowledge/help-aliases.ts'
import { cleanMarkdown } from './clean-markdown.ts'
import { synonyms } from '../knowledge/synonyms.ts'
import { SCHEMA } from '../src/kb.ts'
import type { EntryKind, EntryLink, KnowledgeEntry, Link, LiveIntent } from '../src/types.ts'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const servicehubRepo = resolve(root, process.env.SERVICEHUB_REPO ?? '..')
const outPath = resolve(root, process.env.KB_OUT ?? 'data/knowledge.db')
const docsBaseUrl = process.env.SERVICEHUB_DOCS_URL ?? 'https://github.com/debdevops/servicehub/blob/main'

/** A doc section shorter than this is a heading with a sentence; it answers nothing. */
const MIN_SECTION_CHARS = 120
/** Doc sections are excerpts: enough to answer, then "Read more". */
const MAX_EXCERPT_CHARS = 900

interface Row {
  id: string
  kind: EntryKind
  title: string
  questions: string
  keywords: string
  answer: string
  link?: Link | undefined
  live?: LiveIntent | undefined
  source: string
}

/** The app's Help answer: `next/content/help.ts` in the 5.0.0 layout. `to` is a page id from the app's navigation. */
interface HelpAnswer {
  id: string
  question: string
  answer: string
  link?: { label: string; to: string }
}

/** Where the app's own Help and page list live. Both are read, never written. */
const APP_SRC = 'apps/servicehub/src'
const HELP_FILE = `${APP_SRC}/next/content/help.ts`
const NAVIGATION_FILE = `${APP_SRC}/next/nav/navigation.ts`

/**
 * Links into the app are written as page ids (`replays`, `connections`) and turned into addresses here, from the app's own
 * navigation file, so a renamed or moved page fails the build instead of shipping a dead link. A cloud-scoped page gets
 * the `all` scope; the widget swaps in the cloud the person is looking at.
 */
let appPages: ReadonlyMap<string, string> | undefined

function pageHref(pageId: string, where: string): string {
  appPages ??= readAppPages()
  const path = appPages.get(pageId)
  if (path === undefined) throw new Error(`${where} links to page "${pageId}", which is not in ${NAVIGATION_FILE}`)
  const filled = path.replace(':cloud', 'all')
  if (filled.includes(':')) throw new Error(`${where} links to page "${pageId}" (${path}), which needs more than a cloud to address`)
  return filled ? `/${filled}` : '/'
}

function readAppPages(): Map<string, string> {
  const source = readFileSync(join(servicehubRepo, NAVIGATION_FILE), 'utf8')
  const pages = new Map<string, string>()
  for (const m of source.matchAll(/kind: 'page', id: '([^']+)',[^\n]*?path: '([^']*)'/g)) {
    if (m[1] !== undefined && m[2] !== undefined) pages.set(m[1], m[2])
  }
  if (pages.size === 0) throw new Error(`Found no pages in ${NAVIGATION_FILE}; its format changed, update readAppPages()`)
  return pages
}

function resolveLink(link: EntryLink | undefined, where: string): Link | undefined {
  if (!link) return undefined
  return 'page' in link ? { label: link.label, href: pageHref(link.page, where) } : link
}

async function main(): Promise<void> {
  if (!existsSync(servicehubRepo)) throw new Error(`ServiceHub repo not found at ${servicehubRepo}. Set SERVICEHUB_REPO.`)

  const rows: Row[] = [...curated.map(fromCurated), ...(await fromHelp()), ...fromDocs()]
  const duplicate = rows.map((r) => r.id).find((id, i, all) => all.indexOf(id) !== i)
  if (duplicate) throw new Error(`Duplicate entry id: ${duplicate}`)

  // Built beside the real file, then renamed over it in one step: a bot that opens the file meanwhile (another ./run.sh
  // starting at the same moment) sees the old knowledge base or the new one, never a half-written one.
  mkdirSync(dirname(outPath), { recursive: true })
  const building = `${outPath}.building-${process.pid}`
  rmSync(building, { force: true })
  const db = new DatabaseSync(building)
  try {
    db.exec(SCHEMA)
    db.exec('BEGIN')
    const insert = db.prepare(
      'INSERT INTO entries (id, kind, title, answer, link_label, link_href, live, source) VALUES (?, ?, ?, ?, ?, ?, ?, ?)',
    )
    const insertFts = db.prepare('INSERT INTO entries_fts (rowid, title, questions, keywords, answer) VALUES (?, ?, ?, ?, ?)')
    for (const r of rows) {
      const { lastInsertRowid } = insert.run(
        r.id, r.kind, r.title, r.answer, r.link?.label ?? null, r.link?.href ?? null, r.live ?? null, r.source,
      )
      insertFts.run(lastInsertRowid, r.title, r.questions, r.keywords, r.answer)
    }
    const insertSynonym = db.prepare('INSERT INTO synonyms (term, alternatives) VALUES (?, ?)')
    for (const [term, alternatives] of Object.entries(synonyms)) insertSynonym.run(term, JSON.stringify(alternatives))
    const insertMeta = db.prepare('INSERT INTO meta (key, value) VALUES (?, ?)')
    insertMeta.run('built_at', new Date().toISOString())
    insertMeta.run('servicehub_commit', servicehubCommit())
    for (const kind of ['curated', 'help', 'doc'] as const) {
      insertMeta.run(`count_${kind}`, String(rows.filter((r) => r.kind === kind).length))
    }
    db.exec('COMMIT')
    db.exec("INSERT INTO entries_fts (entries_fts) VALUES ('optimize')")
  } catch (error) {
    db.close()
    rmSync(building, { force: true })
    throw error
  }
  db.close()
  renameSync(building, outPath)

  const count = (k: EntryKind): number => rows.filter((r) => r.kind === k).length
  console.log(
    `Built ${relative(process.cwd(), outPath)}: ${count('curated')} curated, ${count('help')} help, ${count('doc')} doc sections ` +
      `(ServiceHub ${servicehubCommit()}).`,
  )
}

function fromCurated(e: KnowledgeEntry): Row {
  return {
    id: e.id,
    kind: 'curated',
    title: e.title,
    questions: e.questions.join('\n'),
    keywords: (e.keywords ?? []).join('\n'),
    answer: e.answer,
    link: resolveLink(e.link, `entry "${e.id}"`),
    live: e.live,
    source: 'knowledge/entries.ts',
  }
}

/**
 * The app's Help answers, from the 5.0.0 layout's own Help panel. Imported straight from the TypeScript source (Node
 * strips the types). A missing file fails the build: the bot must not quietly lose the product's own words.
 */
async function fromHelp(): Promise<Row[]> {
  const file = join(servicehubRepo, HELP_FILE)
  const { nextHelpAnswers } = (await import(pathToFileURL(file).href)) as { nextHelpAnswers?: readonly HelpAnswer[] }
  if (!nextHelpAnswers?.length) throw new Error(`No nextHelpAnswers exported by ${HELP_FILE}`)
  const stale = Object.keys(helpAliases).filter((id) => !nextHelpAnswers.some((h) => h.id === id))
  if (stale.length) throw new Error(`knowledge/help-aliases.ts names Help answers that no longer exist: ${stale.join(', ')}`)
  return nextHelpAnswers.map((h) => ({
    id: `help:${h.id}`,
    kind: 'help',
    title: h.question,
    questions: [h.question, ...(helpAliases[h.id] ?? [])].join('\n'),
    keywords: '',
    answer: h.answer,
    link: h.link ? { label: h.link.label, href: pageHref(h.link.to, `Help answer "${h.id}"`) } : undefined,
    source: HELP_FILE,
  }))
}

/** Every `##`/`###` section of the public docs, with a link to that heading on GitHub. */
function fromDocs(): Row[] {
  const docsDir = join(servicehubRepo, 'docs')
  const files = readdirSync(docsDir, { recursive: true, encoding: 'utf8' })
    .filter((f) => f.endsWith('.md'))
    .sort()
  const rows: Row[] = []
  for (const file of files) {
    const path = `docs/${file.replaceAll('\\', '/')}`
    const text = readFileSync(join(docsDir, file), 'utf8')
    const docTitle = /^#\s+(.+)$/m.exec(text)?.[1]?.trim() ?? path
    const anchorsSeen = new Map<string, number>()
    for (const section of splitSections(text)) {
      const body = cleanMarkdown(section.body)
      // The text before the first `##` is the doc's introduction: it answers questions about the doc as a whole.
      if (section.heading === null) {
        if (body.length < MIN_SECTION_CHARS) continue
        rows.push({
          id: `doc:${path}`,
          kind: 'doc',
          title: docTitle,
          questions: docTitle,
          keywords: '',
          answer: excerpt(body),
          link: { label: `Read: ${docTitle}`, href: `${docsBaseUrl}/${path}` },
          source: path,
        })
        continue
      }
      // Counted before the length check: GitHub numbers repeated headings whether or not we keep the section.
      const base = slug(section.heading)
      const seen = anchorsSeen.get(base) ?? 0
      anchorsSeen.set(base, seen + 1)
      const anchor = seen === 0 ? base : `${base}-${seen}`
      if (body.length < MIN_SECTION_CHARS) continue
      rows.push({
        id: `doc:${path}#${anchor}`,
        kind: 'doc',
        title: `${section.heading} (${docTitle})`,
        // Both lines count as "how people ask": a question can name the section ("rotate") or the whole doc ("App Service").
        questions: `${section.heading.replace(/^[A-Z]?\d+(\.\d+)*\.?\s+/, '')}\n${docTitle}`,
        keywords: '',
        answer: excerpt(body),
        link: { label: `Read more: ${section.heading}`, href: `${docsBaseUrl}/${path}#${anchor}` },
        source: path,
      })
    }
  }
  return rows
}

/** Splits at `##`/`###` headings outside code blocks. The first section, before any such heading, has `heading: null`. */
function splitSections(markdown: string): { heading: string | null; body: string }[] {
  const sections: { heading: string | null; body: string }[] = []
  let current: { heading: string | null; lines: string[] } = { heading: null, lines: [] }
  let inFence = false
  for (const line of markdown.split('\n')) {
    if (/^\s*(```|~~~)/.test(line)) inFence = !inFence
    const heading = inFence ? null : /^#{2,3}\s+(.+?)\s*#*\s*$/.exec(line)
    if (heading?.[1]) {
      sections.push({ heading: current.heading, body: current.lines.join('\n') })
      current = { heading: heading[1].replace(/[*_`]/g, ''), lines: [] }
    } else if (!(current.heading === null && /^#\s/.test(line))) {
      current.lines.push(line) // the doc's own `# Title` line is not part of its introduction
    }
  }
  sections.push({ heading: current.heading, body: current.lines.join('\n') })
  return sections
}

/** Cuts at a paragraph boundary, never inside a code block. */
function excerpt(body: string): string {
  if (body.length <= MAX_EXCERPT_CHARS) return body
  let out = ''
  for (const paragraph of body.split(/\n\n/)) {
    if (out && out.length + paragraph.length > MAX_EXCERPT_CHARS) break
    out += (out ? '\n\n' : '') + paragraph
  }
  if ((out.match(/^\s*(```|~~~)/gm)?.length ?? 0) % 2 === 1) out += '\n```'
  return `${out}\n\n…`
}

/** GitHub's heading anchor: lowercase, punctuation dropped, spaces to hyphens. */
function slug(heading: string): string {
  return heading
    .toLowerCase()
    .replace(/[^\p{L}\p{N}\s-]/gu, '')
    .trim()
    .replace(/\s/g, '-')
}

function servicehubCommit(): string {
  try {
    return execFileSync('git', ['-C', servicehubRepo, 'rev-parse', '--short', 'HEAD'], { encoding: 'utf8' }).trim()
  } catch {
    return 'unknown'
  }
}

await main()
