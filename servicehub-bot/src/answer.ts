import type { KnowledgeBase } from './kb.ts'
import type { Answer, Hit, LiveIntent, Link } from './types.ts'

/**
 * Builds the reply to one question. The bot guides; it never acts: an answer is information, steps and links that open
 * the right screen. Nothing here, or in anything it calls, changes state in ServiceHub or a cloud.
 */

/** Longer than any real question; a paste beyond this is cut, not rejected. */
export const MAX_QUESTION_CHARS = 500

/**
 * Share of the question's meaningful words the top entry must contain to be given as the answer. Above zero but below:
 * offer the top entries as "Did you mean". 0.6 means a two-word question needs both words, a five-word one three.
 */
export const ANSWER_COVERAGE = 0.6

/** Live numbers for an entry that asks for them (phase 2). Read-only by contract. */
export type LiveProvider = (intent: LiveIntent) => Promise<{ readonly markdown: string; readonly links: readonly Link[] }>

export interface AskOptions {
  readonly live?: LiveProvider
  readonly now?: () => Date
}

const ACTION_VERBS = 'replay|resubmit|resend|retry|redrive|requeue|purge|delete|remove|discard|approve|decline|connect|disconnect|pause|resume|stop|start|enable|disable|send'

/**
 * Whether the person is asking the bot to *do* something ("replay them for me", "can you purge these") rather than asking
 * how. The bot then says plainly that it does not act, and gives the path.
 */
export function asksToAct(question: string): boolean {
  const q = question.toLowerCase().trim()
  return (
    /\b(for me|on my behalf)\b/.test(q) ||
    new RegExp(`^(please\\s+)?(can|could|would|will)\\s+you\\s+(please\\s+)?(${ACTION_VERBS})\\b`).test(q) ||
    new RegExp(`^(please\\s+)?(${ACTION_VERBS})\\s+(it|them|these|those|all|this|that|everything)\\b`).test(q)
  )
}

const DOES_NOT_ACT = "I can't do that for you: I only read and guide, so every action stays yours, with ServiceHub's own preview and safety checks. Here's how to do it:"

const UNKNOWN: Answer = {
  status: 'unknown',
  markdown:
    "I don't have an answer for that yet. I know about ServiceHub: dead letters and replays, connecting clouds, Auto Replay rules, running and hosting ServiceHub, backups and keys. Try asking another way, or open Help.",
  links: [{ label: 'Open Help', href: '?panel=help' }],
  suggestions: ['How do I replay a DLQ?', 'How many dead letters do I have?', 'Which clouds are connected?'],
}

export async function ask(kb: KnowledgeBase, rawQuestion: string, options: AskOptions = {}): Promise<Answer> {
  const question = rawQuestion.trim().slice(0, MAX_QUESTION_CHARS)
  const { hits } = kb.search(question, 4)
  const top = hits[0]

  if (!top || top.coverage === 0) return UNKNOWN

  if (top.coverage < ANSWER_COVERAGE) {
    // Only hand-written and Help entries are offered as "Did you mean": they are questions people ask. A doc heading that
    // shares one word ("write me a poem" → "The two things you write") is noise, so with none of those, say so plainly.
    const questions = hits.filter((h) => h.entry.kind !== 'doc')
    if (questions.length === 0) return UNKNOWN
    return {
      status: 'suggest',
      markdown: "I'm not sure which of these you mean:",
      links: [],
      suggestions: uniqueTitles(questions),
    }
  }

  const entry = top.entry
  const parts: string[] = []
  const links: Link[] = []
  if (asksToAct(question)) parts.push(DOES_NOT_ACT)

  let live: Answer['live']
  if (entry.live && options.live) {
    const asOf = (options.now ?? (() => new Date()))().toISOString()
    try {
      const result = await options.live(entry.live)
      parts.push(result.markdown)
      links.push(...result.links)
      live = { ok: true, asOf }
    } catch (error) {
      // The how-to still stands on its own; say plainly that the numbers are missing and why.
      live = { ok: false, asOf, note: `I couldn't read your live numbers: ${reason(error)}` }
      parts.push(`_${live.note}_`)
    }
  }

  parts.push(entry.answer)
  if (entry.link && !links.some((l) => l.href === entry.link?.href)) links.push(entry.link)

  return {
    status: 'answered',
    title: entry.title,
    markdown: parts.join('\n\n'),
    links,
    // Related questions are hand-written or Help entries only: a doc heading reads badly as a question, and some guide
    // sections still describe 4.2.0 screens. A doc section can still be the answer itself.
    suggestions: uniqueTitles(hits.slice(1).filter((h) => h.entry.kind !== 'doc')).slice(0, 3),
    ...(live ? { live } : {}),
    source: { id: entry.id, kind: entry.kind, ref: entry.source },
  }
}

function uniqueTitles(hits: readonly Hit[]): string[] {
  return [...new Set(hits.map((h) => h.entry.title))]
}

function reason(error: unknown): string {
  return error instanceof Error ? error.message : 'unknown error'
}
