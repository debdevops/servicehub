import { DatabaseSync } from 'node:sqlite'
import { ftsAny, similarity, groups as toGroups } from './text.ts'
import { LIVE_INTENTS } from './types.ts'
import type { EntryKind, Hit, LiveIntent, StoredEntry } from './types.ts'

/**
 * The knowledge base: one SQLite file with an FTS5 index. Built by `scripts/build-kb.ts`, opened read-only here.
 *
 * Schema (also written by `createSchema`):
 *   entries      one row per answer, with where it came from
 *   entries_fts  FTS5 over title, questions, keywords and answer (porter stemming, so "replaying" finds "replay")
 *   synonyms     term → JSON array of alternatives
 *   meta         when and from what the file was built
 */
export const SCHEMA = `
  CREATE TABLE entries (
    rowid      INTEGER PRIMARY KEY,
    id         TEXT NOT NULL UNIQUE,
    kind       TEXT NOT NULL CHECK (kind IN ('curated', 'help', 'doc')),
    title      TEXT NOT NULL,
    answer     TEXT NOT NULL,
    link_label TEXT,
    link_href  TEXT,
    live       TEXT,
    source     TEXT NOT NULL
  );
  CREATE VIRTUAL TABLE entries_fts USING fts5(title, questions, keywords, answer, tokenize = 'porter unicode61');
  CREATE TABLE synonyms (term TEXT PRIMARY KEY, alternatives TEXT NOT NULL);
  CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
`

/** bm25 column weights, in FTS column order: how people ask matters most, the answer body least. */
const COLUMN_WEIGHTS = [5, 8, 4, 1] as const

/** A hand-written answer beats ServiceHub's help text, which beats a raw doc section on the same words. */
const KIND_WEIGHT: Record<EntryKind, number> = { curated: 1.6, help: 1.25, doc: 1 }

/** How many candidates to score before cutting to `limit`. */
const CANDIDATES = 25

/** Weight of closeness to a stored question against relevance (both 0..1). */
const SIMILARITY_WEIGHT = 2

/**
 * A doc section's second question line is its doc's title, shared by every section of that doc. It counts a little less
 * than the section's own heading, so a whole-doc question picks the doc's introduction rather than a random section.
 */
const DOC_TITLE_LINE = 0.8

interface EntryRow {
  rowid: number
  id: string
  kind: EntryKind
  title: string
  answer: string
  link_label: string | null
  link_href: string | null
  live: string | null
  source: string
  questions: string
  rank: number
}

export interface SearchResult {
  /** The question's word groups (term + synonyms), for coverage and for debugging a miss. */
  readonly groups: readonly (readonly string[])[]
  readonly hits: readonly Hit[]
}

export class KnowledgeBase {
  readonly #db: DatabaseSync
  readonly #synonyms: ReadonlyMap<string, readonly string[]>

  private constructor(db: DatabaseSync) {
    this.#db = db
    const rows = db.prepare('SELECT term, alternatives FROM synonyms').all() as { term: string; alternatives: string }[]
    this.#synonyms = new Map(rows.map((r) => [r.term, JSON.parse(r.alternatives) as string[]]))
  }

  /** Opens a built knowledge base read-only. Throws with a hint when the file is missing. */
  static open(path: string): KnowledgeBase {
    let db: DatabaseSync
    try {
      db = new DatabaseSync(path, { readOnly: true })
    } catch (error) {
      throw new Error(`Cannot open the knowledge base at ${path}. Run "npm run build:kb" first.`, { cause: error })
    }
    return new KnowledgeBase(db)
  }

  meta(): Record<string, string> {
    const rows = this.#db.prepare('SELECT key, value FROM meta').all() as { key: string; value: string }[]
    return Object.fromEntries(rows.map((r) => [r.key, r.value]))
  }

  search(question: string, limit = 5): SearchResult {
    const groups = toGroups(question, this.#synonyms)
    if (groups.length === 0) return { groups, hits: [] }

    const rows = this.#db
      .prepare(
        `SELECT e.rowid, e.id, e.kind, e.title, e.answer, e.link_label, e.link_href, e.live, e.source,
                entries_fts.questions AS questions, bm25(entries_fts, ${COLUMN_WEIGHTS.join(', ')}) AS rank
           FROM entries_fts JOIN entries e ON e.rowid = entries_fts.rowid
          WHERE entries_fts MATCH ?
          ORDER BY rank
          LIMIT ${CANDIDATES}`,
      )
      .all(groups.map((g) => `(${ftsAny(g)})`).join(' OR ')) as unknown as EntryRow[]

    const matchOne = this.#db.prepare('SELECT 1 FROM entries_fts WHERE rowid = ? AND entries_fts MATCH ?')
    const best = Math.max(...rows.map((r) => -r.rank * KIND_WEIGHT[r.kind]), Number.EPSILON)
    const hits = rows.map((row): Hit => {
      const matched = groups.filter((g) => matchOne.get(row.rowid, ftsAny(g)) !== undefined).length
      const coverage = matched / groups.length
      // Two signals. Relevance: bm25 (negative, lower is better) weighted by source, scaled so the best candidate is 1.
      // Closeness: how near the question is to one of the entry's own questions (or a doc's heading). Closeness leads,
      // relevance breaks ties, and coverage keeps an entry that answers only part of the question down.
      const relevance = (-row.rank * KIND_WEIGHT[row.kind]) / best
      const closeness = Math.max(
        0,
        ...row.questions.split('\n').map((line, i) => similarity(groups, line) * (row.kind === 'doc' && i > 0 ? DOC_TITLE_LINE : 1)),
      )
      return { entry: toEntry(row), coverage, score: (relevance + SIMILARITY_WEIGHT * closeness) * coverage }
    })
    hits.sort((a, b) => b.score - a.score)
    return { groups, hits: hits.slice(0, limit) }
  }

  close(): void {
    this.#db.close()
  }
}

function toEntry(row: EntryRow): StoredEntry {
  return {
    id: row.id,
    kind: row.kind,
    title: row.title,
    answer: row.answer,
    source: row.source,
    ...(row.link_label && row.link_href ? { link: { label: row.link_label, href: row.link_href } } : {}),
    ...(isLiveIntent(row.live) ? { live: row.live } : {}),
  }
}

function isLiveIntent(value: string | null): value is LiveIntent {
  return value !== null && (LIVE_INTENTS as readonly string[]).includes(value)
}
