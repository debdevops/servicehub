/**
 * A link the widget renders as a button. `href` is either a ServiceHub app address (`/all/replays`: a cloud-scoped page
 * carries the `all` scope, which the widget swaps for the cloud the person is looking at) or a full URL to the docs.
 * It only ever opens a screen; the person does the rest there.
 */
export interface Link {
  readonly label: string
  readonly href: string
}

/**
 * A link as written in `knowledge/entries.ts`: an app page by its id in the app's navigation (`replays`, `connections`),
 * resolved and checked at build time, or a full URL.
 */
export type EntryLink = { readonly label: string; readonly page: string } | { readonly label: string; readonly href: string }

/**
 * The live answers the assistant knows how to build. Each one is a fixed, read-only set of GET calls to ServiceHub's API;
 * the knowledge base only names which one an entry wants, it never holds a URL or a query.
 */
export type LiveIntent = 'dlq-replay' | 'dlq-status' | 'replay-results' | 'connections'

export const LIVE_INTENTS: readonly LiveIntent[] = ['dlq-replay', 'dlq-status', 'replay-results', 'connections']

/** An entry as it is written by hand in `knowledge/entries.ts`. */
export interface KnowledgeEntry {
  readonly id: string
  /** Shown as the answer's heading and as a "Did you mean" suggestion. */
  readonly title: string
  /** The ways people ask this. The strongest search signal, so list real phrasings, short ones included. */
  readonly questions: readonly string[]
  /** Extra words that should find this entry but would read oddly as a question. */
  readonly keywords?: readonly string[]
  /** Markdown. Plain words, a few sentences, the steps in the product. */
  readonly answer: string
  readonly link?: EntryLink
  /** Put live data from the user's own ServiceHub above the answer. */
  readonly live?: LiveIntent
}

/** Where a stored entry came from. Ranking prefers hand-written answers over help text over raw doc sections. */
export type EntryKind = 'curated' | 'help' | 'doc'

/** One row of the knowledge base, as search returns it. */
export interface StoredEntry {
  readonly id: string
  readonly kind: EntryKind
  readonly title: string
  readonly answer: string
  readonly link?: Link
  readonly live?: LiveIntent
  /** Human-readable origin, e.g. `knowledge/entries.ts` or `docs/DOCKER.md`. */
  readonly source: string
}

export interface Hit {
  readonly entry: StoredEntry
  /** Higher is better. Only comparable within one search. */
  readonly score: number
  /** Share (0..1) of the question's meaningful words this entry contains. Decides answer vs "Did you mean". */
  readonly coverage: number
}

export interface Answer {
  /** `answered`: one entry fits. `suggest`: close matches only, shown as choices. `unknown`: nothing fits. */
  readonly status: 'answered' | 'suggest' | 'unknown'
  readonly title?: string
  /** Markdown for the chat bubble. */
  readonly markdown: string
  readonly links: readonly Link[]
  /** Other questions to offer as chips. */
  readonly suggestions: readonly string[]
  /** Present when the entry asked for live data: whether it was read, and when. */
  readonly live?: { readonly ok: boolean; readonly asOf?: string; readonly note?: string }
  readonly source?: { readonly id: string; readonly kind: EntryKind; readonly ref: string }
}
